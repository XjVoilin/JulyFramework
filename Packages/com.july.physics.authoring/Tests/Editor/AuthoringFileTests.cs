using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace July.Physics.Authoring
{
    internal sealed class AuthoringFileTests
    {
        string folder, path;
        [SetUp] public void Setup()
        { folder=Path.Combine(Path.GetTempPath(),"JulyAuthoringFile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"data.json"); }
        [TearDown] public void Cleanup() { Directory.Delete(folder,true); }

        [Test] public void AtomicSaveKeepsExistingReaderSnapshotAndPublishesTheNewFile()
        {
            AuthoringFile.WriteText(path,"old");
            using(var held=AuthoringFile.OpenRead(path))
            {
                AuthoringFile.WriteText(path,"new");
                using var reader=new StreamReader(held,Encoding.UTF8);
                Assert.That(reader.ReadToEnd(),Is.EqualTo("old"));
                Assert.That(AuthoringFile.ReadText(path),Is.EqualTo("new"));
            }
            Assert.That(Directory.GetFiles(folder,"*.tmp"),Is.Empty);
        }
        [Test] public void SerializationFailureKeepsThePreviousFileAndRemovesThePartialTemporary()
        {
            AuthoringFile.WriteText(path,"previous");
            Assert.Throws<IOException>(()=>AuthoringFile.WriteAtomic(path,temp=> {
                File.WriteAllText(temp,"partial");throw new IOException("fixture serialization failure"); }));
            Assert.That(AuthoringFile.ReadText(path),Is.EqualTo("previous"));
            Assert.That(Directory.GetFiles(folder,"*.tmp"),Is.Empty);
        }
        [Test] public void WindowsReadersWithoutDeleteSharingFailObservablyWithoutLosingTheSavedFile()
        {
            if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))Assert.Ignore("Windows file-sharing contract only.");
            AuthoringFile.WriteText(path,"previous");string replacement=Path.Combine(folder,"legacy-replacement.json");
            File.WriteAllText(replacement,"replacement");
            using(var held=File.OpenRead(path))
            {
                var old=Assert.Throws<IOException>(()=>File.Replace(replacement,path,null));
                TestContext.WriteLine($"Old reader ReplaceFile HRESULT: 0x{old.HResult:X8}; {old.Message}");
                Assert.Throws<IOException>(()=>AuthoringFile.WriteText(path,"new"));
                Assert.That(AuthoringFile.ReadText(path),Is.EqualTo("previous"));
            }
            Assert.That(Directory.GetFiles(folder,"*.tmp"),Is.Empty);
            AuthoringFile.WriteText(path,"new");Assert.That(AuthoringFile.ReadText(path),Is.EqualTo("new"));
        }
    }
}
