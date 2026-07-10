using System;

namespace AntShell.Commands
{
    public interface IOperator : ICommand
    {
        char Operator { get; }
    }
}

