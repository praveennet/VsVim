﻿#light

namespace Vim
open System.Text
open System.Text.RegularExpressions

[<System.Flags>]
type VimRegexOptions = 
    | None = 0
    | Compiled = 0x1
    | IgnoreCase = 0x2
    | OrdinalCase = 0x4

module VimRegexUtils = 
    let Escape c = c |> StringUtil.ofChar |> Regex.Escape 

    let ConvertReplacementString (replacement:string) = 
        let builder = StringBuilder()
        let appendChar (c:char) = builder.Append(c) |> ignore
        let appendString (str:string) = builder.Append(str) |> ignore
        let rec inner index = 

            // Process a character which follows an '\' in the string
            let handleEscapeChar c = 
                if CharUtil.IsDigit c then 
                    appendChar '$'
                    appendChar c
                    inner (index + 2)
                else 
                    Escape c |> appendString
                    inner (index + 2)

            match StringUtil.charAtOption index replacement with
            | None -> builder.ToString()
            | Some(c) -> 
                if c = '\\' then 
                    match StringUtil.charAtOption (index + 1) replacement with 
                    | None -> 
                        Escape c |> appendString
                        builder.ToString()
                    | Some(c) -> handleEscapeChar c 
                else 
                    appendChar c
                    inner (index + 1)

        inner 0

    let VimToBclList = 
        [
            (VimRegexOptions.Compiled, RegexOptions.Compiled)
            (VimRegexOptions.IgnoreCase, RegexOptions.IgnoreCase)
            (VimRegexOptions.OrdinalCase, RegexOptions.None)
        ]

    let ConvertToRegexOptions options = 
        
        let rec inner options ret = 
            match List.tryFind (fun (vim,_) -> Utils.IsFlagSet vim options) VimToBclList with
            | None -> ret
            | Some(vim,bcl) ->
                let ret = bcl ||| ret
                let options = Utils.UnsetFlag options vim
                inner options ret 
        inner options RegexOptions.None

    /// Create a regex.  Returns None if the regex has invalid characters
    let TryCreateRegex pattern options =
        try
            let r = new Regex(pattern, options)
            Some r
        with 
            | :? System.ArgumentException -> None

/// Represents a Vim style regular expression 
[<Sealed>]
type VimRegex 
    (
        _vimText : string,
        _regex : Regex ) =

    member x.Text = _vimText
    member x.Regex = _regex
    member x.IsMatch input = _regex.IsMatch(input)
    member x.ReplaceAll (input:string) (replacement:string) = 
        let replacement = VimRegexUtils.ConvertReplacementString replacement
        _regex.Replace(input, replacement) 
    member x.Replace (input:string) (replacement:string) (count:int) = 
        let replacement = VimRegexUtils.ConvertReplacementString replacement
        _regex.Replace(input, replacement, count) 

[<RequireQualifiedAccess>]
type MagicKind = 
    | NoMagic
    | Magic
    | VeryMagic
    | VeryNoMagic

    with

    member x.IsAnyNoMagic =
        match x with 
        | MagicKind.NoMagic -> true
        | MagicKind.VeryNoMagic -> true
        | MagicKind.Magic -> false
        | MagicKind.VeryMagic -> false

    member x.IsAnyMagic = not x.IsAnyNoMagic

type Data = {
    Pattern : string 
    Index : int
    MagicKind : MagicKind 
    MatchCase : bool
    Builder : StringBuilder

    /// Do either the \c or \C atoms appear in the pattern
    HasCaseAtom : bool

    /// Is the match completely broken and should match nothing
    IsBroken : bool

    /// Is this the start of the pattern
    IsStartOfPattern : bool

    /// The original options 
    Options : VimRegexOptions
}
    with
    member x.IsEndOfPattern = x.Index >= x.Pattern.Length
    member x.IncrementIndex count = { x with Index = x.Index + count }
    member x.DecrementIndex count = { x with Index = x.Index - count }
    member x.CharAtIndex = StringUtil.charAtOption x.Index x.Pattern
    member x.AppendString (str:string) = { x with Builder = x.Builder.Append(str) }
    member x.AppendChar (c:char) = { x with Builder = x.Builder.Append(c) }
    member x.AppendEscapedChar c = c |> StringUtil.ofChar |> Regex.Escape |> x.AppendString

[<Sealed>]
type VimRegexFactory
    (
        _settings : IVimGlobalSettings ) =

    member x.Create pattern = x.CreateWithOptions pattern VimRegexOptions.Compiled

    member x.CreateWithOptions pattern options = 
        let kind = if _settings.Magic then MagicKind.Magic else MagicKind.NoMagic
        let data = { 
            Pattern = pattern
            Index = 0
            Builder = new StringBuilder()
            MagicKind = kind
            MatchCase = not _settings.IgnoreCase
            HasCaseAtom = false 
            IsBroken = false 
            IsStartOfPattern = true
            Options = options }

        // Check for smart case here
        let data = 
            let isUpperLetter x = CharUtil.IsLetter x && CharUtil.IsUpper x
            if _settings.SmartCase && data.Pattern |> Seq.filter isUpperLetter |> SeqUtil.isNotEmpty then 
                { data with MatchCase = true }
            else 
                data

        match x.Convert data with
        | None -> None
        | Some(regex) -> VimRegex(pattern,regex) |> Some

    // Create the actual BCL regex 
    member x.CreateRegex (data:Data) =
        let options = VimRegexUtils.ConvertToRegexOptions data.Options

        // Now factor case into the options.  The VimRegexOptions take precedence
        // over anything which is embedded into the string 
        let options = 
            if Utils.IsFlagSet data.Options VimRegexOptions.IgnoreCase then options
            elif Utils.IsFlagSet data.Options VimRegexOptions.OrdinalCase then options
            elif data.MatchCase then options
            else options ||| RegexOptions.IgnoreCase

        if data.IsBroken then None
        else VimRegexUtils.TryCreateRegex (data.Builder.ToString()) options with

    member x.Convert (data:Data) =
        let rec inner (data:Data) : Regex option =
            if data.IsBroken then None
            else
                match data.CharAtIndex with
                | None -> x.CreateRegex data 
                | Some('\\') -> 
                    let data = data.IncrementIndex 1
                    let data = 
                        match data.CharAtIndex with 
                        | None -> x.ProcessNormalChar data '\\'
                        | Some(c) -> x.ProcessEscapedChar (data.IncrementIndex 1) c
                    inner data
                | Some(c) -> x.ProcessNormalChar (data.IncrementIndex 1) c |> inner
        inner data

    /// Process an escaped character.  Look first for global options such as ignore 
    /// case or magic and then go for magic specific characters
    member x.ProcessEscapedChar data c  =
        let escape = VimRegexUtils.Escape
        match c with 
        | 'C' -> {data with MatchCase = true; HasCaseAtom = true}
        | 'c' -> {data with MatchCase = false; HasCaseAtom = true }
        | 'm' -> {data with MagicKind = MagicKind.Magic }
        | 'M' -> {data with MagicKind = MagicKind.NoMagic }
        | 'v' -> {data with MagicKind = MagicKind.VeryMagic }
        | 'V' -> {data with MagicKind = MagicKind.VeryNoMagic }
        | _ ->
            let data = 
                match data.MagicKind with
                | MagicKind.Magic -> x.ConvertEscapedCharAsMagicAndNoMagic data c 
                | MagicKind.NoMagic -> x.ConvertEscapedCharAsMagicAndNoMagic data c
                | MagicKind.VeryMagic -> data.AppendEscapedChar c
                | MagicKind.VeryNoMagic -> x.ConvertCharAsSpecial data c
            {data with IsStartOfPattern=false}
    
    /// Convert a normal unescaped char based on the 
    member x.ProcessNormalChar (data:Data) c = 
        let data = 
            match data.MagicKind with
            | MagicKind.Magic -> x.ConvertCharAsMagic data c
            | MagicKind.NoMagic -> x.ConvertCharAsNoMagic data c
            | MagicKind.VeryMagic -> 
                if CharUtil.IsLetter c || CharUtil.IsDigit c || c = '_' then data.AppendChar c
                else x.ConvertCharAsSpecial data c
            | MagicKind.VeryNoMagic -> data.AppendEscapedChar c
        {data with IsStartOfPattern=false}

    /// Convert the given char in the magic setting 
    member x.ConvertCharAsMagic (data:Data) c =
        match c with 
        | '*' -> data.AppendChar '*'
        | '.' -> data.AppendChar '.'
        | '^' -> x.ConvertCharAsSpecial data c
        | '$' -> x.ConvertCharAsSpecial data c
        | '[' -> x.ConvertCharAsSpecial data c
        | ']' -> x.ConvertCharAsSpecial data c
        | _ -> data.AppendEscapedChar c

    /// Convert the given char in the nomagic setting
    member x.ConvertCharAsNoMagic (data:Data) c =
        match c with 
        | '^' -> x.ConvertCharAsSpecial data c 
        | '$' -> x.ConvertCharAsSpecial data c
        | _ -> data.AppendEscapedChar c

    /// Convert the given escaped char in the magic and no magic settings.  The 
    /// differences here are minimal so it's convenient to put them in one method
    /// here
    member x.ConvertEscapedCharAsMagicAndNoMagic (data:Data) c =
        let isMagic = data.MagicKind = MagicKind.Magic
        match c with 
        | '.' -> if isMagic then data.AppendEscapedChar c else x.ConvertCharAsSpecial data c
        | '*' -> x.ConvertCharAsSpecial data c 
        | '?' -> x.ConvertCharAsSpecial data c 
        | '=' -> x.ConvertCharAsSpecial data c
        | '<' -> x.ConvertCharAsSpecial data c
        | '>' -> x.ConvertCharAsSpecial data c
        | '(' -> x.ConvertCharAsSpecial data c 
        | ')' -> x.ConvertCharAsSpecial data c 
        | '|' -> x.ConvertCharAsSpecial data c
        | '[' -> if isMagic then data.AppendEscapedChar c else x.ConvertCharAsSpecial data c
        | ']' -> x.ConvertCharAsSpecial data c
        | 'd' -> x.ConvertCharAsSpecial data c
        | 'D' -> x.ConvertCharAsSpecial data c
        | 'w' -> x.ConvertCharAsSpecial data c
        | 'W' -> x.ConvertCharAsSpecial data c
        | 'x' -> x.ConvertCharAsSpecial data c
        | 'X' -> x.ConvertCharAsSpecial data c
        | 'o' -> x.ConvertCharAsSpecial data c
        | 'O' -> x.ConvertCharAsSpecial data c
        | 'h' -> x.ConvertCharAsSpecial data c
        | 'H' -> x.ConvertCharAsSpecial data c
        | 'a' -> x.ConvertCharAsSpecial data c
        | 'A' -> x.ConvertCharAsSpecial data c
        | 'l' -> x.ConvertCharAsSpecial data c
        | 'L' -> x.ConvertCharAsSpecial data c
        | 'u' -> x.ConvertCharAsSpecial data c
        | 'U' -> x.ConvertCharAsSpecial data c
        | '_' -> 
            match data.CharAtIndex with
            | None -> { data with IsBroken = true }
            | Some(c) -> 
                let data = data.IncrementIndex 1
                match c with 
                | '^' -> data.AppendChar '^'
                | '$' -> data.AppendChar '$'
                | '.' -> data.AppendString @"(.|\n)"
                | _ -> { data with IsBroken = true }
        | _ -> data.AppendEscapedChar c

    /// Convert the given character as a special character.  Interpretation
    /// may depend on the type of magic that is currently being employed
    member x.ConvertCharAsSpecial (data:Data) c = 
        match c with
        | '.' -> data.AppendChar '.'
        | '=' -> data.AppendChar '?'
        | '?' -> data.AppendChar '?'
        | '*' -> data.AppendChar '*'
        | '(' -> data.AppendChar '('
        | ')' -> data.AppendChar ')'
        | '|' -> data.AppendChar '|'
        | '^' -> if data.IsStartOfPattern then data.AppendChar '^' else data.AppendEscapedChar '^'
        | '$' -> if data.IsEndOfPattern then data.AppendChar '$' else data.AppendEscapedChar '$'
        | '<' -> data.AppendString @"\b"
        | '>' -> data.AppendString @"\b"
        | '[' -> data.AppendChar '['
        | ']' -> data.AppendChar ']'
        | 'd' -> data.AppendString @"\d"
        | 'D' -> data.AppendString @"\D"
        | 'w' -> data.AppendString @"\w"
        | 'W' -> data.AppendString @"\W"
        | 'x' -> data.AppendString @"[0-9A-Fa-f]"
        | 'X' -> data.AppendString @"[^0-9A-Fa-f]"
        | 'o' -> data.AppendString @"[0-7]"
        | 'O' -> data.AppendString @"[^0-7]"
        | 'h' -> data.AppendString @"[A-Za-z_]"
        | 'H' -> data.AppendString @"[^A-Za-z_]"
        | 'a' -> data.AppendString @"[A-Za-z]"
        | 'A' -> data.AppendString @"[^A-Za-z]"
        | 'l' -> data.AppendString @"[a-z]"
        | 'L' -> data.AppendString @"[^a-z]"
        | 'u' -> data.AppendString @"[A-Z]"
        | 'U' -> data.AppendString @"[^A-Z]"
        | _ -> data.AppendEscapedChar c

