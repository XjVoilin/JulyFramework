# 更新记录

## 0.1.1 - 2026-09-12

- 新增构建重载返回本次实际复制的程序集，保留现有返回 bool 的 API。
- 清理输出目录中过期的 DLL 及其 meta，保留当前 DLL 的 GUID 和无关文件。
- 缺失的 AOT 源文件不计入复制结果，避免旧产物混入元数据清单。

## 0.1.0

- Add strongly typed HybridCLR 8.7 installation, generation, DLL copy, AOT backup,
  metadata validation and hot-update build operations.
- Replace the former reflection-based implementation from `com.july.build`.
