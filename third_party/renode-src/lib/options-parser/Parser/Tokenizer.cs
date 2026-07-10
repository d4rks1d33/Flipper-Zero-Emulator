using System;
using System.Linq;
using System.Text;

namespace Antmicro.OptionsParser
{
    public class Tokenizer
    {
        public Tokenizer(string[] input)
        {
            this.input = input;
        }

        public Token ReadNextToken()
        {
            if(position == input.Length)
            {
                return null;
            }
            
            if(state != TokenizerState.PositionalValues && input[position] == EscapeMarker)
            {
                ReadNextString();
                state = TokenizerState.PositionalValues;
            }

            if(state == TokenizerState.PositionalValues)
            {
                return new PositionalArgumentToken(ReadNextString(), GetCurrentPosition());
            }

            var location = GetCurrentPosition();
            var c = ReadChar();
            if(state == TokenizerState.ShortName)
            {
                if(c == Tokenizer.EndOfString)
                {
                    state = TokenizerState.Normal;
                    return ReadNextToken();
                }

                return new ShortNameToken(c, location);
            }

            if(c == FlagCharacter)
            {
                var f = PeekChar();
                if(f == FlagCharacter)
                {
                    // we already peeked it, so just move to the next char
                    ReadChar();
                    var name = ReadUntilChar(out var lastReadChar, Tokenizer.EndOfString, AssignmentOperator);
                    var hasAssignment = lastReadChar == AssignmentOperator;
                    return new LongNameToken(name, hasAssignment, location);
                }

                state = TokenizerState.ShortName;
                return ReadNextToken();
            }
            else
            {
                var value = ReadNextString();
                return (value == null) ? null : new PositionalArgumentToken(value, location);
            }
        }

        public void MoveToTheNextString()
        {
            if(position == input.Length)
            {
                return;
            }

            stringPosition = 0;
            position++;
        }

        public string ReadUntilTheEndOfString()
        {
            if(position == input.Length)
            {
                return null;
            }

            var result = input[position].Substring(stringPosition);
            stringPosition = input[position].Length;
            return result;
        }

        public void MarkPosition()
        {
            markedPosition = position;
            markedStringPosition = stringPosition;
        }

        public void ResetPosition()
        {
            position = markedPosition;
            stringPosition = markedStringPosition;
        }

        public bool Finished { get { return (position == input.Length); } }

        public const char NullCharacter = '\0';
        public const char EndOfString = '\x1';
        public const char FlagCharacter = '-';
        public const string EscapeMarker = "--";

        private char ReadChar()
        {
            if(position == input.Length)
            {
                return Tokenizer.NullCharacter;
            }

            if(stringPosition == input[position].Length)
            {
                stringPosition = 0;
                position++;
                return Tokenizer.EndOfString;
            }

            return input[position].ElementAt(stringPosition++);
        }

        private char PeekChar()
        {
            if(position == input.Length)
            {
                return Tokenizer.NullCharacter;
            }

            if(stringPosition == input[position].Length)
            {
                // we assume that there are no empty strings in 'input'
                return input[position + 1].ElementAt(0);
            }

            return input[position].ElementAt(stringPosition);
        }

        private string ReadUntilChar(out char lastReadChar, params char[] c)
        {
            var builder = new StringBuilder();
            char current = Tokenizer.NullCharacter;
            do
            {
                if(current != Tokenizer.NullCharacter)
                {
                    builder.Append(current);
                }
                current = ReadChar();
                lastReadChar = current;
            }
            while(current != Tokenizer.NullCharacter && !c.Contains(current));

            return builder.ToString();
        }

        private string ReadNextString()
        {
            if(position == input.Length)
            {
                return null;
            }

            stringPosition = 0;
            return input[position++];
        }

        private ElementDescriptor GetCurrentPosition()
        {
            return new ElementDescriptor
            {
                Index = position,
                LocalPosition = stringPosition
            };
        }

        private int position;
        private int stringPosition;
        private int markedPosition;
        private int markedStringPosition;
        private TokenizerState state;
        private readonly string[] input;
      
        private const char Space = ' ';
        private const char AssignmentOperator = '=';

        private enum TokenizerState
        {
            Normal,
            ShortName,
            PositionalValues
        }
    }
}

