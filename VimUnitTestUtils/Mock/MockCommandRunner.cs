﻿using System;
using System.Collections.Generic;
using Microsoft.FSharp.Control;
using Vim;

namespace Vim.UnitTest.Mock
{
    public sealed class MockCommandRunner : ICommandRunner
    {
        public void Add(Command value)
        {
            throw new NotImplementedException();
        }

        public event FSharpHandler<Tuple<CommandRunData, CommandResult>> CommandRan;

        public void RaiseCommandRan(CommandRunData data, CommandResult result)
        {
            var e = CommandRan;
            if (e != null)
            {
                e(this, Tuple.Create(data, result));
            }
        }

        public IEnumerable<Command> Commands
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsWaitingForMoreInput
        {
            get { throw new NotImplementedException(); }
        }

        public void Remove(KeyInputSet value)
        {
            throw new NotImplementedException();
        }

        public void ResetState()
        {
            throw new NotImplementedException();
        }

        public RunKeyInputResult Run(KeyInput value)
        {
            throw new NotImplementedException();
        }

        public CommandRunnerState State
        {
            get { throw new NotImplementedException(); }
        }
    }
}
