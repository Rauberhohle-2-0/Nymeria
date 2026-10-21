using System.Text;
using Nymeria.Core.Models;

namespace Nymeria.Formats.DirectX;

internal enum XTokenKind
{
    Word,
    String,
    OpenBrace,
    CloseBrace,
    Comma,
    Semicolon,
    EndOfInput,
}

internal readonly record struct XToken(XTokenKind Kind, string Text, int LineNumber);

/// <summary>
/// Reads text <c>.x</c> tokens from a character stream one at a time. It tracks
/// line numbers for diagnostics, skips <c>//</c> comments, bounds token length
/// and token count, and never buffers the whole document.
/// </summary>
internal sealed class XTokenReader(
    TextReader reader,
    XImportLimits limits,
    Func<int, string, ModelFormatException> error)
{
    private int _line = 1;
    private int _lookahead = -1;
    private bool _hasLookahead;
    private long _tokenCount;
    private XToken? _peeked;

    public XToken Peek()
    {
        _peeked ??= ReadToken();
        return _peeked.Value;
    }

    public XToken Next()
    {
        if (_peeked is { } token)
        {
            _peeked = null;
            return token;
        }

        return ReadToken();
    }

    private XToken ReadToken()
    {
        if (++_tokenCount > limits.MaxTokenCount)
        {
            throw error(
                _line,
                $"the input contains more than {limits.MaxTokenCount} tokens, " +
                "which exceeds the supported limit.");
        }

        int value = ReadNonWhitespace();

        if (value < 0)
        {
            return new XToken(XTokenKind.EndOfInput, string.Empty, _line);
        }

        return value switch
        {
            '{' => new XToken(XTokenKind.OpenBrace, "{", _line),
            '}' => new XToken(XTokenKind.CloseBrace, "}", _line),
            ',' => new XToken(XTokenKind.Comma, ",", _line),
            ';' => new XToken(XTokenKind.Semicolon, ";", _line),
            '"' => ReadString(),
            _ => ReadWord(value),
        };
    }

    private int ReadNonWhitespace()
    {
        while (true)
        {
            int value = ReadChar();

            if (value < 0)
            {
                return -1;
            }

            if (char.IsWhiteSpace((char)value))
            {
                continue;
            }

            if (value == '/')
            {
                int next = ReadChar();

                if (next == '/')
                {
                    SkipLine();
                    continue;
                }

                if (next >= 0)
                {
                    PushBack(next);
                }

                return '/';
            }

            return value;
        }
    }

    private XToken ReadString()
    {
        int startLine = _line;
        var builder = new StringBuilder();

        while (true)
        {
            int value = ReadChar();

            if (value < 0)
            {
                throw error(
                    startLine,
                    "the string token is not terminated by a closing quote.");
            }

            if (value == '"')
            {
                return new XToken(XTokenKind.String, builder.ToString(), startLine);
            }

            if (value is '\r' or '\n')
            {
                throw error(
                    startLine,
                    "the string token contains a line break before its closing " +
                    "quote.");
            }

            if (builder.Length >= limits.MaxTokenLength)
            {
                throw error(
                    startLine,
                    $"a token is longer than the supported limit of " +
                    $"{limits.MaxTokenLength} characters.");
            }

            builder.Append((char)value);
        }
    }

    private XToken ReadWord(int first)
    {
        int startLine = _line;
        var builder = new StringBuilder();
        builder.Append((char)first);

        while (true)
        {
            int value = PeekChar();

            if (value < 0 ||
                char.IsWhiteSpace((char)value) ||
                value is '{' or '}' or ',' or ';' or '"' or '/')
            {
                break;
            }

            ReadChar();
            builder.Append((char)value);

            if (builder.Length > limits.MaxTokenLength)
            {
                throw error(
                    startLine,
                    $"a token is longer than the supported limit of " +
                    $"{limits.MaxTokenLength} characters.");
            }
        }

        return new XToken(XTokenKind.Word, builder.ToString(), startLine);
    }

    private void SkipLine()
    {
        while (true)
        {
            int value = ReadChar();

            if (value < 0 || value == '\n')
            {
                return;
            }
        }
    }

    private int PeekChar()
    {
        if (!_hasLookahead)
        {
            _lookahead = reader.Read();
            _hasLookahead = true;
        }

        return _lookahead;
    }

    private int ReadChar()
    {
        int value;

        if (_hasLookahead)
        {
            value = _lookahead;
            _hasLookahead = false;
        }
        else
        {
            value = reader.Read();
        }

        if (value == '\n')
        {
            _line++;
        }

        return value;
    }

    private void PushBack(int value)
    {
        if (value == '\n')
        {
            _line--;
        }

        _lookahead = value;
        _hasLookahead = true;
    }
}
