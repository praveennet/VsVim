﻿#light

namespace Vim.Modes.Command
open Vim
open Vim.Modes
open Microsoft.VisualStudio.Text
open System.Text.RegularExpressions
open Vim.RegexUtil

[<System.Flags>]
type internal KeyRemapOptions =
    | None = 0
    | Buffer = 0x1
    | Silent = 0x2
    | Special = 0x4
    | Script = 0x8
    | Expr = 0x10
    | Unique = 0x20

module internal CommandParseUtil = 

    let rec SkipWhitespace (cmd:char list) =
        let inner head tail = 
            if System.Char.IsWhiteSpace head then SkipWhitespace (cmd |> List.tail)
            else cmd
        ListUtil.tryProcessHead cmd inner (fun () -> cmd)
        
    /// Skip past non-whitespace characters and return the string and next input
    let SkipNonWhitespace (cmd:char list) =
        let rec inner (cmd:char list) (data:char list) =
            let withHead headKey rest = 
                if System.Char.IsWhiteSpace headKey then (cmd,data)
                else inner rest (headKey :: data)
            ListUtil.tryProcessHead cmd withHead (fun () -> (cmd,data))
        let rest,data = inner cmd List.empty
        rest,(data |> List.rev |> StringUtil.ofCharSeq)

    /// Try and skip the ! operator
    let SkipBang (cmd:char list) =
        let inner head tail = 
            if head = '!' then (true, tail)
            else (false,cmd)
        ListUtil.tryProcessHead cmd inner (fun () -> (false,cmd))

    /// Parse the register out of the stream.  Will return default if no register is 
    /// specified
    let SkipRegister (map:IRegisterMap) (cmd:char list) =
        let inner head tail =
            match System.Char.IsDigit(head),RegisterNameUtil.CharToRegister head with
            | true,_ -> (map.DefaultRegister, cmd)
            | false,Some(name)-> (map.GetRegister name, tail)
            | false,None -> (map.DefaultRegister, cmd)
        ListUtil.tryProcessHead cmd inner (fun () -> (map.DefaultRegister, cmd))

    let ParseKeyRemapOptions (rest:char list) =
        let rec inner (orig:char list) options =
            let rest,arg = orig |> SkipNonWhitespace
            match arg with
            | "<buffer>" -> inner rest (options ||| KeyRemapOptions.Buffer)
            | "<silent>" -> inner rest (options ||| KeyRemapOptions.Silent)
            | "<special>" -> inner rest (options ||| KeyRemapOptions.Special)
            | "<script>" -> inner rest (options ||| KeyRemapOptions.Script)
            | "<expr>" -> inner rest (options ||| KeyRemapOptions.Expr)
            | "<unique>" -> inner rest (options ||| KeyRemapOptions.Unique)
            | _ -> (orig |> SkipWhitespace,options)
        inner rest KeyRemapOptions.None

    /// Parse out the keys for a key remap command
    let ParseKeys (rest:char list) found notFound =
        let rest,options = rest |> ParseKeyRemapOptions
        let rest,left = rest |> SkipWhitespace |> SkipNonWhitespace
        let rest,right = rest |> SkipWhitespace |> SkipNonWhitespace 
        if System.String.IsNullOrEmpty(left) || System.String.IsNullOrEmpty(right) then
            notFound()
        else
            found left right rest
    

type CommandAction = char list -> Range option -> bool -> unit

/// Type which is responsible for executing command mode commands
type internal CommandProcessor 
    ( 
        _data : IVimBuffer, 
        _operations : IOperations,
        _statusUtil : IStatusUtil,
        _fileSystem : IFileSystem ) as this = 

    let mutable _command : System.String = System.String.Empty

    /// Last substitute operation that occured
    let mutable _lastSubstitute : (string * string * SubstituteFlags) = ("","",SubstituteFlags.None)

    /// List of supported commands.  The bool value on the lambda is whether or not there was a 
    /// bang following the command.  The two strings represent the full and short match name
    /// of the command.  String.Empty represents no shorten'd command available
    let mutable _commandList : (string * string * CommandAction) list = List.empty

    do
        let normalSeq = seq {
            yield ("<", "", this.ProcessShiftLeft)
            yield (">", "", this.ProcessShiftRight)
            yield ("close", "clo", this.ProcessClose)
            yield ("delete","d", this.ProcessDelete)
            yield ("edit", "e", this.ProcessEdit)
            yield ("fold", "fo", this.ProcessFold)
            yield ("join", "j", this.ProcessJoin)
            yield ("marks", "", this.ProcessMarks)
            yield ("put", "pu", this.ProcessPut)
            yield ("quit", "q", this.ProcessQuit)
            yield ("qall", "qa", this.ProcessQuitAll)
            yield ("redo", "red", this.ProcessRedo)
            yield ("set", "se", this.ProcessSet)
            yield ("source","so", this.ProcessSource)
            yield ("split", "sp", this.ProcessSplit)
            yield ("substitute", "s", this.ProcessSubstitute)
            yield ("tabnext", "tabn", this.ProcessTabNext)
            yield ("tabprevious", "tabp", this.ProcessTabPrevious)
            yield ("tabNext", "tabN", this.ProcessTabPrevious)
            yield ("undo", "u", this.ProcessUndo)
            yield ("write","w", this.ProcessWrite)
            yield ("wall", "wa", this.ProcessWriteAll)
            yield ("yank", "y", this.ProcessYank)
            yield ("$", "", fun _ _ _ -> _operations.EditorOperations.MoveToEndOfDocument(false))
            yield ("make", "mak", this.ProcessMake)
        }

        let mapClearSeq = seq {
            yield ("mapclear", "mapc", [KeyRemapMode.Normal; KeyRemapMode.Visual; KeyRemapMode.Command; KeyRemapMode.OperatorPending]);
            yield ("nmapclear", "nmapc", [KeyRemapMode.Normal]);
            yield ("vmapclear", "vmapc", [KeyRemapMode.Visual; KeyRemapMode.Select]);
            yield ("xmapclear", "xmapc", [KeyRemapMode.Visual]);
            yield ("smapclear", "smapc", [KeyRemapMode.Select]);
            yield ("omapclear", "omapc", [KeyRemapMode.OperatorPending]);
            yield ("imapclear", "imapc", [KeyRemapMode.Insert]);
            yield ("cmapclear", "cmapc", [KeyRemapMode.Command]);
        }
        let mapClearSeq = 
            mapClearSeq 
            |> Seq.map (fun (name,short,modes) -> (name, short, fun _ _ hasBang -> this.ProcessKeyMapClear modes hasBang))


        let unmapSeq = seq {
            yield ("unmap", "unm", [KeyRemapMode.Normal;KeyRemapMode.Visual; KeyRemapMode.Select;KeyRemapMode.OperatorPending])
            yield ("nunmap", "nun", [KeyRemapMode.Normal])
            yield ("vunmap", "vu", [KeyRemapMode.Visual;KeyRemapMode.Select])
            yield ("xunmap", "xu", [KeyRemapMode.Visual])
            yield ("sunmap", "sunm", [KeyRemapMode.Select])
            yield ("ounmap", "ou", [KeyRemapMode.OperatorPending])
            yield ("iunmap", "iu", [KeyRemapMode.Insert])
            yield ("lunmap", "lu", [KeyRemapMode.Language])
            yield ("cunmap", "cu", [KeyRemapMode.Command])
        }
        let unmapSeq= 
            unmapSeq
            |> Seq.map (fun (name,short, modes) -> (name,short,(fun rest _ hasBang -> this.ProcessKeyUnmap name modes hasBang rest)))

        let remapSeq = seq {
            yield ("map", "", true, [KeyRemapMode.Normal;KeyRemapMode.Visual; KeyRemapMode.Select;KeyRemapMode.OperatorPending])
            yield ("nmap", "nm", true, [KeyRemapMode.Normal])
            yield ("vmap", "vm", true, [KeyRemapMode.Visual;KeyRemapMode.Select])
            yield ("xmap", "xm", true, [KeyRemapMode.Visual])
            yield ("smap", "", true, [KeyRemapMode.Select])
            yield ("omap", "om", true, [KeyRemapMode.OperatorPending])
            yield ("imap", "im", true, [KeyRemapMode.Insert])
            yield ("lmap", "lm", true, [KeyRemapMode.Language])
            yield ("cmap", "cm", true, [KeyRemapMode.Command])
            yield ("noremap", "no", false, [KeyRemapMode.Normal;KeyRemapMode.Visual; KeyRemapMode.Select;KeyRemapMode.OperatorPending])
            yield ("nnoremap", "nn", false, [KeyRemapMode.Normal])
            yield ("vnoremap", "vn", false, [KeyRemapMode.Visual;KeyRemapMode.Select])
            yield ("xnoremap", "xn", false, [KeyRemapMode.Visual])
            yield ("snoremap", "snor", false, [KeyRemapMode.Select])
            yield ("onoremap", "ono", false, [KeyRemapMode.OperatorPending])
            yield ("inoremap", "ino", false, [KeyRemapMode.Insert])
            yield ("lnoremap", "ln", false, [KeyRemapMode.Language])
            yield ("cnoremap", "cno", false, [KeyRemapMode.Command])
        }

        let remapSeq = 
            remapSeq 
            |> Seq.map (fun (name,short,allowRemap,modes) -> (name,short,(fun rest _ hasBang -> this.ProcessKeyMap name allowRemap modes hasBang rest)))

        _commandList <- 
            normalSeq 
            |> Seq.append remapSeq
            |> Seq.append mapClearSeq
            |> Seq.append unmapSeq
            |> List.ofSeq

#if DEBUG
        // Make sure there are no duplicates
        let set = new System.Collections.Generic.HashSet<string>()
        for name in _commandList |> Seq.map (fun (name,_,_) -> name) do
            if not (set.Add(name)) then failwith (sprintf "Duplicate command name %s" name)
        let set = new System.Collections.Generic.HashSet<string>()
        for name in _commandList |> Seq.map (fun (_,short,_) -> short) do
            if name <> "" && not (set.Add(name)) then failwith (sprintf "Duplicate command short name %s" name)

#endif

    member private x.BadMessage = Resources.CommandMode_CannotRun _command

    /// Process the :close command
    member private x.ProcessClose _ _ hasBang = _data.Vim.VimHost.CloseView _data.TextView (not hasBang)

    /// Process the :join command
    member private x.ProcessJoin (rest:char list) (range:Range option) hasBang =
        let kind = if hasBang then JoinKind.KeepEmptySpaces else JoinKind.RemoveEmptySpaces
        let rest = CommandParseUtil.SkipWhitespace rest
        let count,rest = RangeUtil.ParseNumber rest
        let span = 
            match range with 
            | Some(range) -> Some(RangeUtil.GetSnapshotSpan range)
            | None -> None
            
        let range = span
        let range = 
            match range with 
            | Some(s) -> s
            | None -> 
                let point = TextViewUtil.GetCaretPoint _data.TextView
                SnapshotSpan(point,0)

        match count with 
        | Some(c) -> _operations.Join range.End kind c |> ignore
        | None -> 
            let startLine = range.Start.GetContainingLine().LineNumber
            let endLine = range.End.GetContainingLine().LineNumber
            let count = endLine - startLine
            _operations.Join range.Start kind count |> ignore

    /// Parse out the :edit commnad
    member private x.ProcessEdit (rest:char list) _ hasBang = 
        let name = 
            rest 
                |> CommandParseUtil.SkipWhitespace
                |> StringUtil.ofCharSeq
        if System.String.IsNullOrEmpty name then _operations.ShowOpenFileDialog()
        else _operations.EditFile name

    /// Parse out the fold command and create the fold
    member private x.ProcessFold _ (range : Range option) _ =
        let span = RangeUtil.RangeOrCurrentLine _data.TextView range |> RangeUtil.GetSnapshotSpan
        _operations.FoldManager.CreateFold span

    /// Parse out the Yank command
    member private x.ProcessYank (rest:char list) (range: Range option) _ =
        let reg,rest = rest |> CommandParseUtil.SkipRegister _data.RegisterMap
        let count,rest = RangeUtil.ParseNumber rest

        // Calculate the span to yank
        let range = RangeUtil.RangeOrCurrentLine _data.TextView range
        
        // Apply the count if present
        let range = 
            match count with             
            | Some(count) -> RangeUtil.ApplyCount range count
            | None -> range

        let span = RangeUtil.GetSnapshotSpan range
        _operations.Yank span MotionKind.Exclusive OperationKind.LineWise reg

    /// Parse the Put command
    member private x.ProcessPut (rest:char list) (range: Range option) bang =
        let reg,rest = 
            rest
            |> CommandParseUtil.SkipWhitespace
            |> CommandParseUtil.SkipRegister _data.RegisterMap
        
        // Figure out the line number
        let line = 
            match range with 
            | None -> (TextViewUtil.GetCaretPoint _data.TextView).GetContainingLine()
            | Some(range) ->
                match range with 
                | Range.SingleLine(line) -> line
                | Range.RawSpan(span) -> span.End.GetContainingLine()
                | Range.Lines(tss,_,endLine) -> tss.GetLineFromLineNumber(endLine)

        _operations.Put reg.StringValue line (not bang)

    /// Parse the < command
    member private x.ProcessShiftLeft (rest:char list) (range: Range option) _ =
        let count,rest =  rest  |> RangeUtil.ParseNumber
        let range = RangeUtil.RangeOrCurrentLine _data.TextView range
        let range = 
            match count with
            | Some(count) -> RangeUtil.ApplyCount range count
            | None -> range
        let span = RangeUtil.GetSnapshotSpan range
        _operations.ShiftSpanLeft 1 span 

    member private x.ProcessShiftRight (rest:char list) (range: Range option) _ =
        let count,rest = rest |> RangeUtil.ParseNumber
        let range = RangeUtil.RangeOrCurrentLine _data.TextView range
        let range = 
            match count with
            | Some(count) -> RangeUtil.ApplyCount range count
            | None -> range
        let span = RangeUtil.GetSnapshotSpan range
        _operations.ShiftSpanRight 1 span 

    member private x.ProcessWrite (rest:char list) _ _ = 
        let name = rest |> StringUtil.ofCharSeq 
        let name = name.Trim()
        if StringUtil.isNullOrEmpty name then _operations.Save()
        else _operations.SaveAs name

    member private x.ProcessWriteAll _ _ _ = 
        _operations.SaveAll()

    member private x.ProcessQuit _ _ hasBang = _data.Vim.VimHost.CloseView _data.TextView (not hasBang)

    member private x.ProcessQuitAll _ _ hasBang =
        let checkDirty = not hasBang
        _operations.CloseAll checkDirty

    member private x.ProcessTabNext rest _ _ =
        let count,rest = RangeUtil.ParseNumber rest
        let count = match count with | Some(c) -> c | None -> 1
        _operations.GoToNextTab count

    member private x.ProcessTabPrevious rest _ _ =
        let count,rest = RangeUtil.ParseNumber rest
        let count = match count with | Some(c) -> c | None -> 1
        _operations.GoToPreviousTab count

    member private x.ProcessMake _ _ bang =
        _data.Vim.VimHost.BuildSolution()

    /// Implements the :delete command
    member private x.ProcessDelete (rest:char list) (range:Range option) _ =
        let reg,rest = rest |> CommandParseUtil.SkipRegister _data.RegisterMap
        let count,rest = rest |> CommandParseUtil.SkipWhitespace |> RangeUtil.ParseNumber
        let range = RangeUtil.RangeOrCurrentLine _data.TextView range
        let range = 
            match count with
            | Some(count) -> RangeUtil.ApplyCount range count
            | None -> range
        let span = RangeUtil.GetSnapshotSpan range
        _operations.DeleteSpan span MotionKind.Exclusive OperationKind.LineWise reg |> ignore

    member private x.ProcessUndo rest _ _ =
        match Seq.isEmpty rest with
        | true -> _operations.Undo 1
        | false -> _statusUtil.OnError x.BadMessage

    member private x.ProcessRedo rest _ _ =
        match Seq.isEmpty rest with
        | true -> _operations.Redo 1
        | false -> _statusUtil.OnError x.BadMessage

    member private x.ProcessMarks rest _ _ =
        match Seq.isEmpty rest with
        | true -> _operations.PrintMarks _data.MarkMap
        | false -> _statusUtil.OnError x.BadMessage

    /// Parse out the :set command
    member private x.ProcessSet (rest:char list) _ _=
        let rest,data = rest |> CommandParseUtil.SkipNonWhitespace
        if System.String.IsNullOrEmpty(data) then _operations.PrintModifiedSettings()
        else
            match data with
            | Match1("^all$") _ -> _operations.PrintAllSettings()
            | Match2("^(\w+)\?$") (_,name) -> _operations.PrintSetting name
            | Match2("^no(\w+)$") (_,name) -> _operations.ResetSetting name
            | Match2("^(\w+)\!$") (_,name) -> _operations.InvertSetting name
            | Match2("^inv(\w+)$") (_,name) -> _operations.InvertSetting name
            | Match3("^(\w+):(\w+)$") (_,name,value) -> _operations.SetSettingValue name value
            | Match3("^(\w+)=(\w+)$") (_,name,value) -> _operations.SetSettingValue name value
            | Match2("^(\w+)$") (_,name) -> _operations.OperateSetting(name)
            | _ -> ()

    /// Used to parse out the :source command.  List is pointing past the o in :source
    member private x.ProcessSource (rest:char list) _ bang =
        let file = rest |> StringUtil.ofCharSeq
        if bang then _statusUtil.OnError Resources.CommandMode_NotSupported_SourceNormal
        else
            match _fileSystem.ReadAllLines file with
            | None -> _statusUtil.OnError (Resources.CommandMode_CouldNotOpenFile file)
            | Some(lines) ->
                lines 
                |> Seq.map (fun command -> command |> List.ofSeq)
                |> Seq.iter x.RunCommand

    /// Split the given view into 2
    member private x.ProcessSplit _ _ _ =
        _data.Vim.VimHost.SplitView _data.TextView

    /// Process the :substitute command. 
    member private x.ProcessSubstitute(rest:char list) (range:Range option) _ =

        // Used to parse out the flags on the :s command
        let rec parseFlags (rest:char seq) =
            let charToOption c = 
                match c with 
                | 'c' -> SubstituteFlags.Confirm
                | '&' -> SubstituteFlags.UsePrevious
                | 'e' -> SubstituteFlags.SuppressError
                | 'g' -> SubstituteFlags.ReplaceAll
                | 'i' -> SubstituteFlags.IgnoreCase
                | 'I' -> SubstituteFlags.OrdinalCase
                | 'n' -> SubstituteFlags.ReportOnly
                | _ -> SubstituteFlags.Invalid
            rest |> Seq.fold (fun f c -> (charToOption c) ||| f) SubstituteFlags.None 

        let doParse rest badParse goodParse =

            // Parse one element out of the sequence.  We expect the rest to 
            // be pointed at a string prefixed with '/'.  "found" will be called
            // with both the text after the '/' and the remainder of the string
            let parseOne rest notFound found =
                match rest with
                | [] -> notFound()
                | h::t -> 
                    if h <> '/' then notFound()
                    else 
                        let rest = t |> Seq.ofList
                        let data = rest |> Seq.takeWhile (fun c -> c <> '/') |> StringUtil.ofCharSeq
                        found data (t |> ListUtil.skip data.Length)

            parseOne rest (fun () -> badParse() ) (fun search rest -> 
                parseOne rest (fun () -> badParse()) (fun replace rest ->  
                    let rest = rest |> ListUtil.skipWhile (fun c -> c = '/')
                    let flagsInput = rest |> Seq.takeWhile (fun c -> not (CharUtil.IsWhiteSpace c))
                    let flags = parseFlags flagsInput
                    let flags = 
                        if Utils.IsFlagSet flags SubstituteFlags.UsePrevious then
                            let _,_,prev = _lastSubstitute
                            (Utils.UnsetFlag flags SubstituteFlags.UsePrevious) ||| prev
                        else
                            flags
                    if Utils.IsFlagSet flags SubstituteFlags.Invalid then badParse()
                    else goodParse search replace flags ))

        let range = RangeUtil.RangeOrCurrentLine _data.TextView range |> RangeUtil.GetSnapshotSpan
        if List.isEmpty rest then
            let search,replace,flags = _lastSubstitute
            _operations.Substitute search replace range flags
        else 
            let badParse () = _statusUtil.OnError Resources.CommandMode_InvalidCommand
            let goodParse search replace flags = 
                if Utils.IsFlagSet flags SubstituteFlags.Confirm then
                    _statusUtil.OnError Resources.CommandMode_NotSupported_SubstituteConfirm
                else
                    _operations.Substitute search replace range flags
                    _lastSubstitute <- (search,replace,flags)
            doParse rest badParse goodParse    
    
    member private x.ProcessKeyMapClear modes hasBang =
        let modes = 
            if hasBang then [KeyRemapMode.Insert; KeyRemapMode.Command]
            else modes
        _operations.ClearKeyMapModes modes

    member private x.ProcessKeyUnmap (name:string) (modes: KeyRemapMode list) (hasBang:bool) (rest: char list) = 
        let modes = 
            if hasBang then [KeyRemapMode.Insert; KeyRemapMode.Command]
            else modes
        let rest,lhs = rest |> CommandParseUtil.SkipNonWhitespace
        _operations.UnmapKeys lhs modes
        
    member private x.ProcessKeyMap (name:string) (allowRemap:bool) (modes: KeyRemapMode list) (hasBang:bool) (rest: char list) = 
        let modes = 
            if hasBang then [KeyRemapMode.Insert; KeyRemapMode.Command]
            else modes
        let withKeys lhs rhs _ = _operations.RemapKeys lhs rhs modes allowRemap 
        CommandParseUtil.ParseKeys rest withKeys (fun() -> _statusUtil.OnError x.BadMessage)

    member private x.ParseCommand (rest:char list) (range:Range option) = 

        let isCommandNameChar c = 
            (not (System.Char.IsWhiteSpace(c))) 
            && c <> '!'
            && c <> '/'

        // Get the name of the command
        let commandName = 
            rest 
            |> Seq.takeWhile isCommandNameChar
            |> StringUtil.ofCharSeq

        // Look for commands with that name
        let command =
            
            // First look for the exact match
            let found =
                _commandList
                |> Seq.tryFind (fun (name,shortName,_) -> name = commandName || shortName = commandName)
            match found,StringUtil.isNullOrEmpty commandName with
            | _,true -> None
            | Some(data),false -> Some(data)
            | None,false ->
                
                // No exact name matches look for a prefix match on the full command name
                let found =
                    _commandList
                    |> Seq.filter (fun (name,_,_) -> name.StartsWith(commandName, System.StringComparison.Ordinal))
                match found |> Seq.length with
                | 1 -> found |> Seq.head |> Some
                | _ -> None


        match command with
        | None -> _statusUtil.OnError x.BadMessage
        | Some(name,shortName,action) ->
            let rest = rest |> ListUtil.skip commandName.Length 
            let hasBang,rest = rest |> CommandParseUtil.SkipBang
            let rest = rest |> CommandParseUtil.SkipWhitespace
            action rest range hasBang
    
    member private x.ParseInput (originalInputs :char list) =
        let withRange (range:Range option) (inputs:char list) = x.ParseCommand inputs range
        let point = TextViewUtil.GetCaretPoint _data.TextView
        match RangeUtil.ParseRange point _data.MarkMap originalInputs with
        | ParseRangeResult.Succeeded(range, inputs) -> 
            if inputs |> List.isEmpty then
                match range with 
                | SingleLine(line) -> _operations.EditorOperations.GotoLine(line.LineNumber) |> ignore
                | _ -> _statusUtil.OnError("Invalid Command String")
            else
                withRange (Some(range)) inputs
        | NoRange -> withRange None originalInputs
        | ParseRangeResult.Failed(msg) -> _statusUtil.OnError msg

    /// Run the specified command.  This funtion can be called recursively
    member x.RunCommand (input: char list)=
        let prev = _command
        try
            // Strip off the preceeding :
            let input = 
                match ListUtil.tryHead input with
                | None -> input
                | Some(head,tail) when head = ':' -> tail
                | _ -> input

            _command <- input |> StringUtil.ofCharSeq
            x.ParseInput input
        finally
            _command <- prev

    interface ICommandProcessor with
        member x.RunCommand input = x.RunCommand input

            


