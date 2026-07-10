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
using AntShell.Helpers;
using AntShell.Terminal;
using System.IO;

namespace AntShell
{
    public class CommandLine : ITerminalHandler
    {
        #region Private fields

        private Mode mode;
        private CommandEditor command;
        private CommandEditor search;
        private CommandEditor userInput;
        private NavigableTerminalEmulator terminal;

        public char DirectorySeparatorChar { get; set; }

        public Func<string, string> PreprocessSuggestionsInput { get; set; }

        public Prompt NormalPrompt { get; set; }

        private SearchPrompt _reverseSearchPrompt;

        private SearchPrompt _forwardSearchPrompt;

        public SearchPrompt ReverseSearchPrompt
        {
            get
            {
                return _reverseSearchPrompt;
            }

            set
            {
                _reverseSearchPrompt = value;
                _reverseSearchPrompt.SetCommandEditor(search);
            }
        }

        public SearchPrompt ForwardSearchPrompt
        {
            get
            {
                return _forwardSearchPrompt;
            }

            set
            {
                _forwardSearchPrompt = value;
                _forwardSearchPrompt.SetCommandEditor(search);
            }
        }

        public SearchPrompt SearchPrompt => history.searchForward ? _forwardSearchPrompt : _reverseSearchPrompt;

        private CommandHistory history;
        private ICommandHandler handler;

        private bool tabTabMode = false;

        #endregion

        public CommandLine(NavigableTerminalEmulator term, CommandHistory history, ICommandHandler handler)
        {
            this.handler = handler;
            this.history = history;
            command = new CommandEditor();
            search = new CommandEditor();
            userInput = new CommandEditor();

            terminal = term;
            term.Handler = this;
        }

        public void Start()
        {
            CurrentPrompt.Write(terminal);
        }

        private CommandEditor CurrentEditor
        {
            get
            {
                switch(mode)
                {
                case Mode.Command:
                    return command;
                case Mode.Search:
                    return search;
                case Mode.UserInput:
                    return userInput;
                default:
                    return null;
                }
            }
        }

        internal Prompt CurrentPrompt
        {
            get
            {
                switch(mode)
                {
                case Mode.Command:
                    return NormalPrompt;
                case Mode.Search:
                    return SearchPrompt;
                default:
                    return null;
                }
            }
        }

        #region Input handlers

        private void HandleSearchPromptChange()
        {
            SearchPrompt.Recreate(terminal);

            history.Reset();
            var result = history.Search(search.Value);
            terminal.WriteNoMove(result, SearchPrompt.Skip);
        }

        private void RedrawCommandLine()
        {
            if(mode == Mode.Command || mode == Mode.UserInput)
            {
                terminal.ClearToTheEndOfLine();
                terminal.WriteNoMove(CurrentEditor.ToString(CurrentEditor.Position));
            }
            else if(mode == Mode.Search)
            {
                HandleSearchPromptChange();
            }
        }

        private void RemoveWord()
        {
            var diff = CurrentEditor.RemoveWord();
            if(diff > 0)
            {
                terminal.CursorBackward(diff);
                RedrawCommandLine();
            }
        }

        private void RemoveWordForward()
        {
            var diff = CurrentEditor.RemoveWordForward();
            if(diff > 0)
            {
                RedrawCommandLine();
            }
        }

        private void HistoryPrevious()
        {
            if(!history.HasMoved)
            {
                history.SetCurrentCommand(CurrentEditor.Value);
            }

            var prev = history.PreviousCommand();
            if(prev != null)
            {
                var len = CurrentEditor.Position;
                CurrentEditor.SetValue(prev);
                terminal.CursorBackward(len);
                terminal.ClearToTheEndOfLine();
                terminal.Write(CurrentEditor.Value);
            }
        }

        private void HistoryNext()
        {
            var cmd = history.NextCommand();
            if(cmd != null)
            {
                var len = CurrentEditor.Position;
                CurrentEditor.SetValue(cmd);
                terminal.CursorBackward(len);
                terminal.ClearToTheEndOfLine();
                terminal.Write(CurrentEditor.Value);
            }
        }

        public void HandleControlSequence(ControlSequence seq)
        {
            switch(seq.Type)
            {
            case ControlSequenceType.Home:
                {
                    var diff = CurrentEditor.MoveHome();
                    terminal.CursorBackward(diff);
                }
                break;

            case ControlSequenceType.End:
                {
                    var diff = CurrentEditor.MoveEnd();
                    terminal.CursorForward(diff);
                }
                break;

            case ControlSequenceType.LeftArrow:
                if(CurrentEditor.MoveCharacterBackward())
                {
                    terminal.CursorBackward();
                }
                break;

            case ControlSequenceType.RightArrow:
                if(CurrentEditor.MoveCharacterForward())
                {
                    terminal.CursorForward();
                }
                break;

            case ControlSequenceType.UpArrow:
                if(mode == Mode.UserInput)
                {
                    break;
                }
                HistoryPrevious();
                break;

            case ControlSequenceType.DownArrow:
                if(mode == Mode.UserInput)
                {
                    break;
                }
                HistoryNext();
                break;

            case ControlSequenceType.Backspace:
                if(CurrentEditor.RemovePreviousCharacter())
                {
                    terminal.CursorBackward();

                    if(mode == Mode.Command || mode == Mode.UserInput)
                    {
                        terminal.ClearToTheEndOfLine();
                        terminal.WriteNoMove(CurrentEditor.ToString(CurrentEditor.Position));
                    }
                    else if(mode == Mode.Search)
                    {
                        HandleSearchPromptChange();
                    }
                }
                break;

            case ControlSequenceType.AltBackspace:
                RemoveWord();
                break;

            case ControlSequenceType.Delete:
                if(CurrentEditor.RemoveNextCharacter())
                {
                    if(mode == Mode.Command || mode == Mode.UserInput)
                    {
                        terminal.ClearToTheEndOfLine();
                        terminal.WriteNoMove(CurrentEditor.ToString(CurrentEditor.Position));
                    }
                    else if(mode == Mode.Search)
                    {
                        HandleSearchPromptChange();
                    }
                }
                break;

            case ControlSequenceType.AltD:
                RemoveWordForward();
                break;

            case ControlSequenceType.CtrlLeftArrow:
                {
                    var diff = CurrentEditor.MoveWordBackward();
                    terminal.CursorBackward(diff);
                }
                break;

            case ControlSequenceType.CtrlRightArrow:
                {
                    var diff = CurrentEditor.MoveWordForward();
                    terminal.CursorForward(diff);
                }
                break;

            case ControlSequenceType.Ctrl:
                var searchForward = false;
                switch((char)seq.Argument)
                {
                case 'a':
                    {
                        var diff = CurrentEditor.MoveHome();
                        terminal.CursorBackward(diff);
                    }
                    break;

                case 'e':
                    {
                        var diff = CurrentEditor.MoveEnd();
                        terminal.CursorForward(diff);
                    }
                    break;

                case 'f':
                    if(CurrentEditor.MoveCharacterForward())
                    {
                        terminal.CursorForward();
                    }
                    break;

                case 'b':
                    if(CurrentEditor.MoveCharacterBackward())
                    {
                        terminal.CursorBackward();
                    }
                    break;

                case 'p':
                    if(mode == Mode.UserInput)
                    {
                        break;
                    }
                    HistoryPrevious();
                    break;

                case 'n':
                    if(mode == Mode.UserInput)
                    {
                        break;
                    }
                    HistoryNext();
                    break;

                case 'c':
                    if(mode == Mode.Command)
                    {
                        terminal.CursorForward(CurrentEditor.MoveEnd());
                        terminal.Write("^C");
                        terminal.NewLine();
                    }
                    else
                    {
                        mode = Mode.Command;
                        terminal.ClearLine();
                    }

                    search.Clear();
                    command.Clear();

                    NormalPrompt.Write(terminal);
                    history.Reset();

                    break;

                case 's':
                    searchForward = true;
                    goto case 'r';
                case 'r':
                    if(mode == Mode.UserInput)
                    {
                        break;
                    }
                    else if(mode == Mode.Command)
                    {
                        history.searchForward = searchForward;
                        mode = Mode.Search;
                        terminal.CursorBackward(command.MoveHome());
                        terminal.ClearLine();
                        search.SetValue(string.Empty);
                        command.Clear();

                        CurrentPrompt.Write(terminal);
                    }
                    else if(mode == Mode.Search)
                    {
                        if(search.Value != string.Empty)
                        {
                            if(history.searchForward != searchForward)
                            {
                                history.searchForward = searchForward;
                                terminal.ClearLine();
                                CurrentPrompt.Write(terminal);
                            }
                            var result = history.Search(search.Value);
                            terminal.CursorForward(SearchPrompt.Skip);
                            terminal.ClearToTheEndOfLine();
                            terminal.WriteNoMove(result);
                            terminal.CursorBackward(SearchPrompt.Skip);
                        }
                    }

                    break;

                case 'w':
                    RemoveWord();
                    break;

                case 'k':
                    CurrentEditor.RemoveToTheEnd();

                    if(mode == Mode.Command || mode == Mode.UserInput)
                    {
                        terminal.ClearToTheEndOfLine();
                    }
                    else if(mode == Mode.Search)
                    {
                        HandleSearchPromptChange();
                    }
                    break;

                case 'd':
                    // Delete next character
                    if(CurrentEditor.RemoveNextCharacter())
                    {
                        terminal.ClearToTheEndOfLine();
                        terminal.WriteNoMove(CurrentEditor.ToString(CurrentEditor.Position));
                        
                        // For Search also update prompt
                        if(mode == Mode.Search)
                        {
                            HandleSearchPromptChange();
                        }
                    }
                    // Empty line? Then quit current mode, or the terminal
                    // for UserInput this needs to be handled in `ReadLine`
                    else if(CurrentEditor.Length == 0 && mode != Mode.UserInput)
                    {
                        if(mode == Mode.Command)
                        {
                            terminal.Write("^D");
                            terminal.NewLine();
                            terminal.Stop();
                        }
                        else if(mode == Mode.Search)
                        {
                            // Do not close the terminal, just exit the search prompt
                            mode = Mode.Command;
                            terminal.ClearLine();
                            CurrentPrompt.Write(terminal);
                        }
                    }
                    break;

                case 'u':
                    // Delete all characters to the beginning of the line
                    while(CurrentEditor.RemovePreviousCharacter())
                    {
                        terminal.CursorBackward();
                    }
                    if(mode == Mode.Command || mode == Mode.UserInput)
                    {
                        terminal.ClearToTheEndOfLine();
                        terminal.WriteNoMove(CurrentEditor.ToString(CurrentEditor.Position));
                    }
                    else if(mode == Mode.Search)
                    {
                        HandleSearchPromptChange();
                    }
                    break;

                default:
                    break;
                }
                break;

            case ControlSequenceType.Esc:
                if(mode == Mode.Search)
                {
                    search.Clear();
                    command.SetValue(history.CurrentCommand ?? string.Empty);
                    mode = Mode.Command;

                    terminal.ClearLine();
                    NormalPrompt.Write(terminal);
                    terminal.Write(command.Value);
                }
                break;

            case ControlSequenceType.Tab:
                if(mode == Mode.UserInput)
                {
                    break;
                }
                else if(mode == Mode.Search)
                {
                    mode = Mode.Command;
                    CurrentEditor.SetValue(history.CurrentCommand);
                    terminal.ClearLine();
                    CurrentPrompt.Write(terminal);
                    terminal.Write(CurrentEditor.Value);
                }
                else if(mode == Mode.Command)
                {
                    var sugs = handler.SuggestionNeeded(CurrentEditor.Value);
                    var preparedBaseString = PreprocessSuggestionsInput(CurrentEditor.Value);
                    var commonPrefix = Helper.CommonPrefix(sugs, preparedBaseString);
                    var prefix = String.IsNullOrEmpty(commonPrefix) ? CurrentEditor.Value : commonPrefix;

                    if(sugs.Length == 0)
                    {
                        break;
                    }

                    if(!tabTabMode || sugs.Length == 1)
                    {
                        tabTabMode = true;

                        terminal.CursorBackward(CurrentEditor.Position);
                        terminal.ClearToTheEndOfLine();
                        CurrentEditor.SetValue(prefix);
                    }
                    else if(tabTabMode)
                    {
                        terminal.NewLine();

                        var splitPoint = prefix.LastIndexOf(" ", StringComparison.Ordinal);
                        foreach(var sug in sugs)
                        {
                            terminal.WriteRaw(string.Format(" {0}\r\n", sug.Substring(splitPoint + 1)));
                        }
                        CurrentEditor.SetValue(prefix);
                        NormalPrompt.Write(terminal);
                    }

                    if(sugs.Length == 1 && sugs[0][sugs[0].Length - 1] != DirectorySeparatorChar)
                    {
                        CurrentEditor.InsertCharacter(' ');
                    }
                    terminal.Write(CurrentEditor.Value);
                    return;
                }

                break;

            case ControlSequenceType.Enter:
                var wasInSearchMode = false;
                if(mode == Mode.Search)
                {
                    mode = Mode.Command;
                    command.SetValue(history.CurrentCommand ?? string.Empty);
                    search.Clear();

                    terminal.ClearLine();
                    wasInSearchMode = true;
                }
                else
                {
                    terminal.CursorForward(CurrentEditor.MoveEnd());
                    terminal.NewLine();
                }

                if(CurrentEditor.Value != string.Empty)
                {
                    if(wasInSearchMode)
                    {
                        NormalPrompt.Write(terminal);
                        terminal.Write(command.Value);
                        terminal.NewLine();
                    }

                    var cmd = CurrentEditor.Value;
                    while(true)
                    {
                        var interaction = handler.HandleCommand(cmd, null);
                        if(interaction != null && interaction.QuitEnvironment)
                        {
                            terminal.Stop();
                            return;
                        }

                        CurrentEditor.Clear();
                        if(interaction != null && interaction.CommandToExecute != null)
                        {
                            cmd = interaction.CommandToExecute;
                            terminal.Write(cmd);
                            terminal.NewLine();
                            (interaction as CommandInteraction).Clear();
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                NormalPrompt.Write(terminal);
                if(CurrentEditor.Length > 0)
                {
                    terminal.Write(CurrentEditor.Value);
                }
                history.Reset();

                break;

            default:
                break;
            }

            tabTabMode = false;
        }

        public void HandleCharacter(char character)
        {
            if(char.IsDigit(character) || char.IsLetter(character) || char.IsSymbol(character) || char.IsPunctuation(character) || char.IsWhiteSpace(character))
            {
                var appended = CurrentEditor.InsertCharacter(character);

                if(mode != Mode.Search)
                {
                    terminal.Write(character);

                    if(!appended)
                    {
                        terminal.WriteNoMove(CurrentEditor.ToString(CurrentEditor.Position));
                    }
                }
                else
                {
                    SearchPrompt.Recreate(terminal, -1);

                    history.Reset();
                    var result = history.Search(search.Value);
                    terminal.WriteNoMove(result, SearchPrompt.Skip);
                }
            }

            tabTabMode = false;
        }

        #endregion

        public string ReadLine()
        {
            var currentMode = mode;
            mode = Mode.UserInput;
            string result = null;

            while(true)
            {
                var input = terminal.GetNextInput();
                var inputAsControlSequence = input as ControlSequence;
                if(input is char)
                {
                    HandleCharacter((char)input);
                }
                else if(inputAsControlSequence != null)
                {
                    if(inputAsControlSequence.Type == ControlSequenceType.Enter)
                    {
                        result = CurrentEditor.Value;
                        break;
                    }
                    else if(inputAsControlSequence.Type == ControlSequenceType.Ctrl)
                    {
                        // This handles 'UserInput' mode for Ctrl-C and Ctrl-D
                        if((char)inputAsControlSequence.Argument == 'c')
                        {
                            terminal.CursorForward(CurrentEditor.MoveEnd());
                            terminal.Write("^C");
                            break;
                        } 
                        else if((char)inputAsControlSequence.Argument == 'd')
                        {
                            if(String.IsNullOrEmpty(CurrentEditor.Value))
                            {
                                terminal.CursorForward(CurrentEditor.MoveEnd());
                                terminal.Write("^D");
                                break;
                            }
                        }
                    }
                    HandleControlSequence((ControlSequence)input);
                }
            }

            CurrentEditor.Clear();
            terminal.NewLine();

            mode = currentMode;
            return result;
        }

        private enum Mode
        {
            Command,
            Search,
            UserInput
        }
    }
}
