﻿#light

namespace Vim

open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text.Operations

type internal ChangeTracker
    (   _textChangeTrackerFactory : ITextChangeTrackerFactory ) =

    let mutable _last : RepeatableChange option = None
    
    member private x.OnVimBufferCreated (buffer:IVimBuffer) =
        buffer.NormalMode.CommandRunner.CommandRan |> Event.add x.OnCommandRan
        buffer.VisualLineMode.CommandRunner.CommandRan |> Event.add x.OnCommandRan
        buffer.VisualBlockMode.CommandRunner.CommandRan |> Event.add x.OnCommandRan
        buffer.VisualCharacterMode.CommandRunner.CommandRan |> Event.add x.OnCommandRan

        let tracker = _textChangeTrackerFactory.GetTextChangeTracker buffer
        tracker.ChangeCompleted |> Event.add x.OnTextChanged 

    member private x.OnCommandRan ((data:CommandRunData),_) = 
        let command = data.Command
        if command.IsMovement || command.IsSpecial then
            // Movement and special commandsd don't participate in change tracking
            ()
        elif command.IsRepeatable then _last <- CommandChange data |> Some
        else _last <- None

    member private x.OnTextChanged data = 
        let useCurrent() = _last <- TextChange(data) |> Some
        match _last with
        | None -> _last <- TextChange(data) |> Some
        | Some(last) ->
            match last with
            | LinkedChange(_) -> useCurrent()
            | TextChange(_) -> useCurrent()
            | CommandChange(change) -> 
                if Utils.IsFlagSet change.Command.CommandFlags CommandFlags.LinkedWithNextTextChange then
                    let change = LinkedChange (CommandChange change,TextChange data) 
                    _last <- Some change
                else useCurrent()
            
    interface IVimBufferCreationListener with
        member x.VimBufferCreated buffer = x.OnVimBufferCreated buffer

    interface IChangeTracker with
        member x.LastChange = _last
