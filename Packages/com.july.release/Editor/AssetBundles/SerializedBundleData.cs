using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    // 使用当前 Editor 自带的 UnityFileSystemApi 读取真实写出文件；不加载 Unity 资源，也不估算内存。
    internal sealed class SerializedBundleData : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct ObjectInfo { internal long Id, Offset, Size; internal int TypeId; }
        internal sealed class ObjectData
        {
            internal long Id, Offset, Bytes;
            internal string File, Type, Note;
            internal readonly List<StreamRange> Streams = new List<StreamRange>();
            internal readonly List<ObjectReference> AtlasTextures = new List<ObjectReference>();
        }
        internal sealed class StreamRange { internal string File; internal long Offset, Bytes; }
        internal sealed class ObjectReference { internal string File; internal long Id; }
        sealed class Node
        {
            internal string Type, Name;
            internal int Size, Flags, Meta;
            internal readonly List<Node> Children = new List<Node>();
            internal bool Align => (Meta & 0x4000) != 0;
            // Child alignment can add padding beyond the declared size (for example MeshBlendShape is 10 bytes, but occupies 12).
            internal bool CanSkipBytes => Size >= 0 && (Meta & 0x8000) == 0;
            internal bool Interesting;
        }
        sealed class UnsupportedLayout : Exception { internal UnsupportedLayout(string type) : base("尚未支持的序列化布局：" + type) { } }

        readonly bool _ownsInitialization;
        static IntPtr _library;
        public SerializedBundleData()
        {
            if (_library == IntPtr.Zero)
            {
                var tools = Path.Combine(EditorApplication.applicationContentsPath, "Tools");
                if (Application.platform == RuntimePlatform.WindowsEditor) _library = LoadLibrary(Path.Combine(tools, "UnityFileSystemApi.dll"));
                else _library = dlopen(Path.Combine(tools, Application.platform == RuntimePlatform.OSXEditor ? "UnityFileSystemApi.dylib" : "UnityFileSystemApi.so"), 2);
                if (_library == IntPtr.Zero) throw new NotSupportedException("当前 Unity Editor 无法加载 Tools/UnityFileSystemApi，不能统计包内资源大小。");
            }
            var code = Init();
            if (code != 0 && code != 1) Check(code, "初始化 Unity 数据读取器");
            _ownsInitialization = code == 0;
        }
        public void Dispose() { if (_ownsInitialization) Check(Cleanup(), "关闭 Unity 数据读取器"); }

        internal ObjectData[] Read(string path, string alias)
        {
            Check(OpenSerializedFile(path, out var handle), "打开 " + path);
            try
            {
                Check(GetObjectCount(handle, out var count), "读取对象数");
                var objects = new ObjectInfo[count];
                Check(GetObjectInfo(handle, objects, count), "读取对象表");
                Check(GetExternalReferenceCount(handle, out var referenceCount), "读取外部引用数");
                var references = new string[referenceCount];
                for (int i = 0; i < references.Length; i++)
                {
                    var name = new StringBuilder(4096); var guid = new StringBuilder(33);
                    Check(GetExternalReference(handle, i, name, name.Capacity, guid, out _), "读取外部引用");
                    references[i] = Alias(name.ToString());
                }
                var trees = new Dictionary<IntPtr, Node>();
                var result = new List<ObjectData>();
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    // 当前支持的 Unity 平台写出文件使用小端对象数据；文件头的 endian 标记是稳定格式的一部分。
                    stream.Position = 16;
                    if (reader.ReadByte() != 0) throw new NotSupportedException("暂不支持大端 SerializedFile：" + path);
                    foreach (var info in objects)
                    {
                        if (info.Offset < 0 || info.Size < 0 || info.Offset > stream.Length - info.Size) throw new InvalidDataException("对象数据超出文件范围：" + path);
                        var data = new ObjectData { Id = info.Id, Offset = info.Offset, Bytes = info.Size, File = alias, Type = info.TypeId.ToString() };
                        result.Add(data);
                        var code = GetTypeTree(handle, info.Id, out var treeHandle);
                        if (code == 12) { data.Note = "构建未保留类型树，流数据尚未统计"; continue; }
                        Check(code, "读取类型树");
                        if (!trees.TryGetValue(treeHandle, out var tree)) trees.Add(treeHandle, tree = ReadNode(treeHandle, 0));
                        data.Type = tree.Type;
                        if (!tree.Interesting) continue;
                        stream.Position = info.Offset;
                        try { Walk(reader, tree, info.Offset + info.Size, data, references, false); }
                        catch (UnsupportedLayout exception) { data.Note = exception.Message; }
                        catch (InvalidDataException exception) { throw new InvalidDataException($"读取 {alias} / {data.Type} #{data.Id}，位置 {stream.Position - info.Offset}/{info.Size}：{exception.Message}", exception); }
                    }
                }
                return result.ToArray();
            }
            finally { Check(CloseSerializedFile(handle), "关闭 " + path); }
        }
        internal static string Alias(string path) => path.Replace('\\', '/').Substring(path.Replace('\\', '/').LastIndexOf('/') + 1);

        static Node ReadNode(IntPtr tree, int index)
        {
            var type = new StringBuilder(1024); var name = new StringBuilder(1024);
            Check(GetTypeTreeNodeInfo(tree, index, type, type.Capacity, name, name.Capacity, out _, out var size, out var flags, out var meta, out var child, out _), "读取类型节点");
            var node = new Node { Type = type.ToString(), Name = name.ToString(), Size = size, Flags = flags, Meta = meta };
            while (child != 0)
            {
                node.Children.Add(ReadNode(tree, child));
                Check(GetTypeTreeNodeInfo(tree, child, type, type.Capacity, name, name.Capacity, out _, out _, out _, out _, out _, out child), "读取类型节点关系");
            }
            node.Interesting = IsStream(node) || node.Name == "m_RenderDataMap";
            foreach (var item in node.Children) node.Interesting |= item.Interesting;
            return node;
        }
        static void Walk(BinaryReader reader, Node node, long end, ObjectData data, string[] references, bool atlas)
        {
            atlas |= data.Type == "SpriteAtlas" && node.Name == "m_RenderDataMap";
            if (IsStream(node))
            {
                var range = new StreamRange();
                foreach (var child in node.Children)
                {
                    if (child.Name == "path" || child.Name == "m_Source") range.File = ReadString(reader, end);
                    else if (child.Name == "offset" || child.Name == "m_Offset") range.Offset = ReadInteger(reader, child, end);
                    else if (child.Name == "size" || child.Name == "m_Size") range.Bytes = ReadInteger(reader, child, end);
                    else Walk(reader, child, end, data, references, atlas);
                    if (child.Align) Align(reader, end);
                }
                if (range.Bytes > 0)
                {
                    if (string.IsNullOrEmpty(range.File) || range.Offset < 0) throw new InvalidDataException("流数据位置无效");
                    range.File = Alias(range.File); data.Streams.Add(range);
                }
            }
            else if (atlas && node.Type == "PPtr<Texture2D>")
            {
                Require(reader, end, 12); var fileId = reader.ReadInt32(); var id = reader.ReadInt64();
                if (id != 0)
                {
                    if (fileId < 0 || fileId > references.Length) throw new InvalidDataException("图集纹理引用无效");
                    data.AtlasTextures.Add(new ObjectReference { File = fileId == 0 ? data.File : references[fileId - 1], Id = id });
                }
            }
            else if (!node.Interesting && !atlas && node.CanSkipBytes) Skip(reader, node.Size, end);
            else if (node.Type == "string") { var length = ReadCount(reader, end); Skip(reader, length, end); Align(reader, end); }
            else if ((node.Flags & 1) != 0)
            {
                if (node.Children.Count != 2) throw new UnsupportedLayout(node.Type);
                int length = ReadCount(reader, end); var element = node.Children[1];
                if (!element.Interesting && !atlas && element.CanSkipBytes && !element.Align) Skip(reader, checked((long)element.Size * length), end);
                else for (int i = 0; i < length; i++) Walk(reader, element, end, data, references, atlas);
            }
            else if (node.Children.Count > 0) foreach (var child in node.Children) Walk(reader, child, end, data, references, atlas);
            else if (node.Size >= 0) Skip(reader, node.Size, end);
            else throw new UnsupportedLayout(node.Type);
            if (node.Align) Align(reader, end);
        }
        static bool IsStream(Node node) => node.Type == "StreamingInfo" || node.Type == "StreamedResource" || node.Type == "StreamingInfo64";
        static int ReadCount(BinaryReader reader, long end) { Require(reader, end, 4); var count = reader.ReadInt32(); if (count < 0) throw new InvalidDataException("负数数组长度"); return count; }
        static string ReadString(BinaryReader reader, long end) { var length = ReadCount(reader, end); Require(reader, end, length); var text = Encoding.UTF8.GetString(reader.ReadBytes(length)); Align(reader, end); return text; }
        static long ReadInteger(BinaryReader reader, Node node, long end)
        {
            Require(reader, end, node.Size);
            if (node.Size == 8) return checked((long)reader.ReadUInt64());
            if (node.Size == 4) return reader.ReadUInt32();
            throw new UnsupportedLayout(node.Type);
        }
        static void Skip(BinaryReader reader, long length, long end) { Require(reader, end, length); reader.BaseStream.Position += length; }
        static void Align(BinaryReader reader, long end) { var padding = (4 - (reader.BaseStream.Position & 3)) & 3; Skip(reader, padding, end); }
        static void Require(BinaryReader reader, long end, long length) { if (length < 0 || reader.BaseStream.Position > end - length) throw new InvalidDataException("序列化字段超出对象范围"); }
        static void Check(int code, string operation) { if (code != 0) throw new InvalidDataException(operation + "失败，UnityFileSystemApi 返回 " + code); }

        [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW")] static extern IntPtr LoadLibrary(string path);
        [DllImport("__Internal", EntryPoint = "dlopen")] static extern IntPtr dlopen(string path, int mode);
        const string Dll = "UnityFileSystemApi";
        [DllImport(Dll, EntryPoint = "UFS_Init", CallingConvention = CallingConvention.Cdecl)] static extern int Init();
        [DllImport(Dll, EntryPoint = "UFS_Cleanup", CallingConvention = CallingConvention.Cdecl)] static extern int Cleanup();
        [DllImport(Dll, EntryPoint = "UFS_OpenSerializedFile", CallingConvention = CallingConvention.Cdecl)] static extern int OpenSerializedFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr handle);
        [DllImport(Dll, EntryPoint = "UFS_CloseSerializedFile", CallingConvention = CallingConvention.Cdecl)] static extern int CloseSerializedFile(IntPtr handle);
        [DllImport(Dll, EntryPoint = "UFS_GetObjectCount", CallingConvention = CallingConvention.Cdecl)] static extern int GetObjectCount(IntPtr handle, out int count);
        [DllImport(Dll, EntryPoint = "UFS_GetObjectInfo", CallingConvention = CallingConvention.Cdecl)] static extern int GetObjectInfo(IntPtr handle, [In, Out] ObjectInfo[] objects, int count);
        [DllImport(Dll, EntryPoint = "UFS_GetExternalReferenceCount", CallingConvention = CallingConvention.Cdecl)] static extern int GetExternalReferenceCount(IntPtr handle, out int count);
        [DllImport(Dll, EntryPoint = "UFS_GetExternalReference", CallingConvention = CallingConvention.Cdecl)] static extern int GetExternalReference(IntPtr handle, int index, StringBuilder path, int capacity, StringBuilder guid, out int type);
        [DllImport(Dll, EntryPoint = "UFS_GetTypeTree", CallingConvention = CallingConvention.Cdecl)] static extern int GetTypeTree(IntPtr handle, long id, out IntPtr tree);
        [DllImport(Dll, EntryPoint = "UFS_GetTypeTreeNodeInfo", CallingConvention = CallingConvention.Cdecl)] static extern int GetTypeTreeNodeInfo(IntPtr tree, int node, StringBuilder type, int typeLength, StringBuilder name, int nameLength, out int offset, out int size, out int flags, out int meta, out int child, out int next);
    }
}
