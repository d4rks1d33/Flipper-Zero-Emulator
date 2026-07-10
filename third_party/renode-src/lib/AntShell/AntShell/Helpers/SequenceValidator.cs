/*
Copyright (c) 2013 Ant Micro <www.antmicro.com>

Authors:
* Mateusz Holenko (mholenko@antmicro.com)

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using System;
using System.Collections.Generic;

namespace AntShell.Helpers
{
    public class SequenceValidator
    {
        private Dictionary<char, Tuple<SequenceValidator, ControlSequenceGenerator>> seqs = new Dictionary<char, Tuple<SequenceValidator, ControlSequenceGenerator>>();

        public SequenceValidationResult Check(IReadOnlyList<char> input, out ControlSequence seq)
        {
            Tuple<ControlSequenceGenerator, int> result;
            var seqFound = TryCheck(input, 0, out result);
            if(seqFound)
            {
                if(result != null)
                {
                    var length = result.Item2 + 1;
                    seq = result.Item1(input, length);
                    return SequenceValidationResult.SequenceFound;
                }
                else
                {
                    seq = null;
                    return SequenceValidationResult.PrefixFound;
                }
            }
            else
            {
                seq = null;
                return SequenceValidationResult.SequenceNotFound;
            }

        }

        private bool TryCheck(IReadOnlyList<char> input, int offset, out Tuple<ControlSequenceGenerator, int> seq)
        {
            if(offset == input.Count)
            {
                seq = null;
                return true;
            }

            if(seqs.ContainsKey((char)255))
            {
                var loffset = offset;

                if(char.IsDigit((char)input[loffset]))
                {
                    while(loffset < input.Count && char.IsDigit((char)input[loffset]))
                    {
                        if(loffset < input.Count)
                        {
                            loffset++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    var result = seqs[(char)255].Item1.TryCheck(input, loffset, out seq); // might not work when '255' byte is the last one in a sequence
                    if(result)
                    {
                        return result;
                    }
                }
            }

            if(seqs.ContainsKey(input[offset]))
            {
                if(input.Count - offset == 1)
                {
                    if(seqs[input[offset]].Item2 != null)
                    {
                        seq = Tuple.Create(seqs[input[offset]].Item2, offset);
                        return true;
                    }
                    else
                    {
                        seq = null;
                        return true;
                    }
                }

                return seqs[input[offset]].Item1.TryCheck(input, offset + 1, out seq);
            }
            else
            {
                seq = null;
                return false;
            }
        }

        public void Add(ControlSequenceGenerator seq, params char[] input)
        {
            Add(input, 0, seq);
        }

        public void Add(ControlSequence seq, params char[] input)
        {
            Add(input, 0, (a, b) => seq);
        }

        public void Add(ControlSequenceType seqT, params char[] input)
        {
            Add(input, 0, (a, b) => new ControlSequence(seqT));
        }

        private void Add(char[] input, int offset, ControlSequenceGenerator seq)
        {
            if(input.Length - offset == 0)
            {
                return;
            }

            if(!seqs.ContainsKey(input[offset]))
            {
                seqs.Add(input[offset], new Tuple<SequenceValidator, ControlSequenceGenerator>(new SequenceValidator(), input.Length - offset == 1 ? seq : null));
            }

            if(input.Length - offset == 1)
            {
                return;
            }
            else
            {
                seqs[input[offset]].Item1.Add(input, offset + 1, seq);
            }
        }
    }

    public enum SequenceValidationResult
    {
        SequenceNotFound,
        SequenceFound,
        PrefixFound
    }

    public delegate ControlSequence ControlSequenceGenerator(IReadOnlyList<char> seq,int length);
}

