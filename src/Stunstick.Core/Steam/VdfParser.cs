using System.Text;

namespace Stunstick.Core.Steam;

public static class VdfParser
{
	public static VdfObject ParseFile(string path, Encoding? encoding = null)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		encoding ??= Encoding.UTF8;
		var text = File.ReadAllText(path, encoding);
		return Parse(text);
	}

	public static VdfObject Parse(string text)
	{
		if (text is null)
		{
			throw new ArgumentNullException(nameof(text));
		}

		if (text.Length > 0 && text[0] == '\uFEFF')
		{
			text = text[1..];
		}

		var tokenizer = new Tokenizer(text);
		var obj = ParseObject(tokenizer, endOnRBrace: false);

		var token = tokenizer.Peek();
		if (token.Kind is not TokenKind.Eof)
		{
			throw new InvalidDataException("Unexpected trailing tokens in VDF.");
		}

		return obj;
	}

	private static VdfObject ParseObject(Tokenizer tokenizer, bool endOnRBrace)
	{
		var properties = new Dictionary<string, VdfValue>(StringComparer.Ordinal);

		while (true)
		{
			var token = tokenizer.Peek();
			if (token.Kind == TokenKind.Eof)
			{
				break;
			}

			if (token.Kind == TokenKind.RBrace)
			{
				if (!endOnRBrace)
				{
					throw new InvalidDataException("Unexpected '}' in VDF.");
				}

				tokenizer.Read(); // consume
				break;
			}

			var key = tokenizer.ReadString();
			var next = tokenizer.Peek();

			if (next.Kind == TokenKind.LBrace)
			{
				tokenizer.Read(); // consume '{'
				var child = ParseObject(tokenizer, endOnRBrace: true);
				properties[key] = child;
				continue;
			}

			var value = tokenizer.ReadString();
			properties[key] = new VdfString(value);
		}

		return new VdfObject(properties);
	}

	private enum TokenKind
	{
		String,
		LBrace,
		RBrace,
		Eof
	}

	private readonly record struct Token(TokenKind Kind, string? Value);

	private sealed class Tokenizer
	{
		private readonly string text;
		private int index;
		private Token? peeked;

		public Tokenizer(string text)
		{
			this.text = text;
		}

		public Token Peek()
		{
			peeked ??= ReadNextToken();
			return peeked.Value;
		}

		public Token Read()
		{
			if (peeked is not null)
			{
				var token = peeked.Value;
				peeked = null;
				return token;
			}

			return ReadNextToken();
		}

		public string ReadString()
		{
			var token = Read();
			if (token.Kind != TokenKind.String)
			{
				throw new InvalidDataException($"Expected string but found {token.Kind}.");
			}

			return token.Value ?? string.Empty;
		}

		private Token ReadNextToken()
		{
			SkipWhitespaceAndComments();

			if (index >= text.Length)
			{
				return new Token(TokenKind.Eof, null);
			}

			var ch = text[index];
			if (ch == '{')
			{
				index++;
				return new Token(TokenKind.LBrace, null);
			}

			if (ch == '}')
			{
				index++;
				return new Token(TokenKind.RBrace, null);
			}

			if (ch == '"')
			{
				return new Token(TokenKind.String, ReadQuotedString());
			}

			return new Token(TokenKind.String, ReadBareString());
		}

		private void SkipWhitespaceAndComments()
		{
			while (index < text.Length)
			{
				var ch = text[index];
				if (char.IsWhiteSpace(ch))
				{
					index++;
					continue;
				}

				if (ch == '/' && index + 1 < text.Length && text[index + 1] == '/')
				{
					index += 2;
					while (index < text.Length && text[index] is not '\n' and not '\r')
					{
						index++;
					}

					continue;
				}

				break;
			}
		}

		private string ReadQuotedString()
		{
			if (text[index] != '"')
			{
				throw new InvalidOperationException("Tokenizer is not positioned on a quoted string.");
			}

			index++; // opening quote
			var sb = new StringBuilder();

			while (index < text.Length)
			{
				var ch = text[index++];
				if (ch == '"')
				{
					break;
				}

				if (ch == '\\' && index < text.Length)
				{
					var next = text[index++];
					sb.Append(next switch
					{
						'\\' => '\\',
						'"' => '"',
						'n' => '\n',
						'r' => '\r',
						't' => '\t',
						_ => next
					});
					continue;
				}

				sb.Append(ch);
			}

			return sb.ToString();
		}

		private string ReadBareString()
		{
			var start = index;
			while (index < text.Length)
			{
				var ch = text[index];
				if (char.IsWhiteSpace(ch) || ch is '{' or '}')
				{
					break;
				}

				index++;
			}

			return text[start..index];
		}
	}
}
