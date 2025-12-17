using System;
using System.IO;

namespace TextFileWatch;

public sealed class FileDocument
{
    private readonly string _path;
    private string _text = string.Empty;
    private DateTime _lastWriteTimeUtc;
    private long _lastLength;
    private bool _hasSnapshot;

    public FileDocument(string path)
    {
        _path = path;
    }

    public string Path => _path;
    public string Text => _text;

    public DocumentUpdateResult TryRefresh(bool force)
    {
        DateTime writeTimeUtc;
        long length;
        try
        {
            if (!File.Exists(_path))
                return DocumentUpdateResult.Fail("File does not exist.");

            var info = new FileInfo(_path);
            writeTimeUtc = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch (Exception ex)
        {
            return DocumentUpdateResult.Fail(ex.Message);
        }

        if (!force && _hasSnapshot && writeTimeUtc == _lastWriteTimeUtc && length == _lastLength)
            return DocumentUpdateResult.NoChange();

        string newText;
        try
        {
            newText = File.ReadAllText(_path);
        }
        catch (Exception ex)
        {
            return DocumentUpdateResult.Fail(ex.Message);
        }

        var oldText = _text;
        _text = newText;
        _lastWriteTimeUtc = writeTimeUtc;
        _lastLength = length;
        _hasSnapshot = true;

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return DocumentUpdateResult.NoChange();

        return DocumentUpdateResult.WithChange(oldText, newText);
    }
}

public readonly struct DocumentUpdateResult
{
    private DocumentUpdateResult(bool success, bool changed, string? errorMessage, string oldText, string newText)
    {
        Success = success;
        Changed = changed;
        ErrorMessage = errorMessage;
        OldText = oldText;
        NewText = newText;
    }

    public bool Success { get; }
    public bool Changed { get; }
    public string? ErrorMessage { get; }
    public string OldText { get; }
    public string NewText { get; }

    public static DocumentUpdateResult NoChange() => new(success: true, changed: false, errorMessage: null, oldText: string.Empty, newText: string.Empty);
    public static DocumentUpdateResult WithChange(string oldText, string newText) => new(success: true, changed: true, errorMessage: null, oldText: oldText, newText: newText);
    public static DocumentUpdateResult Fail(string errorMessage) => new(success: false, changed: false, errorMessage: errorMessage, oldText: string.Empty, newText: string.Empty);
}
