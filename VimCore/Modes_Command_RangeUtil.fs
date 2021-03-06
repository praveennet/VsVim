﻿#light

namespace Vim.Modes.Command
open Vim
open Microsoft.VisualStudio.Text

type internal Range = 
    | RawSpan of SnapshotSpan
    /// Start and End line of the range
    | Lines of ITextSnapshot * int * int
    | SingleLine of ITextSnapshotLine

type internal ParseRangeResult =
    | Succeeded of Range * char list
    | NoRange 
    | Failed of string 

type internal ItemRangeKind = 
    | LineNumber
    | CurrentLine
    | Mark

type internal ItemRange = 
    | ValidRange of Range * ItemRangeKind * char list
    | NoRange
    | Error of string

type internal RangeParser() =
    member this.Bind (ir, rest) =
        match ir with 
        | ValidRange (r,kind,input) -> rest (r,kind,input)
        | NoRange -> ParseRangeResult.NoRange
        | Error(msg) -> Failed(msg)
    member this.Bind (pr, rest) = 
        match pr with
        | Succeeded (range,remaining) -> rest (range,remaining)
        | ParseRangeResult.NoRange -> pr
        | Failed(_) -> pr
    member this.Zero () = Failed "Invalid"
    member this.ReturnFrom (result:ParseRangeResult) = result

module internal RangeUtil =

    let private _parser = RangeParser()

    /// Get the SnapshotSpan for the given Range
    let GetSnapshotSpan (r:Range) =
        match r with
        | RawSpan(span) -> span
        | Lines(tss,first,last) -> 
            new SnapshotSpan(
                tss.GetLineFromLineNumber(first).Start,
                tss.GetLineFromLineNumber(last).EndIncludingLineBreak)
        | SingleLine(line) -> line.ExtentIncludingLineBreak
        
    /// Get the range for the currently selected line
    let RangeForCurrentLine view =
        let point = TextViewUtil.GetCaretPoint view
        let line = point.GetContainingLine()
        Range.SingleLine(line)

    /// Retrieve the passed in range if valid or the range for the current line
    /// if the Range Option is empty
    let RangeOrCurrentLine view rangeOpt =
        match rangeOpt with
        | Some(range) -> range
        | None -> RangeForCurrentLine view

    /// Apply the count to the given range
    let ApplyCount range count =
        let count = if count <= 1 then 1 else count-1
        let inner (tss:ITextSnapshot) startLine =
            let endLine = startLine + count
            let endLine = if endLine >= tss.LineCount then tss.LineCount-1 else endLine
            Range.Lines(tss,startLine,endLine)
            
        match range with 
        | Range.Lines(tss,_,endLine) ->
            // When a cuont is applied to a line range, the count of lines staring at the end 
            // line is used
            inner tss endLine
        | Range.SingleLine(line) -> inner line.Snapshot line.LineNumber
        | Range.RawSpan(span) -> 
            inner span.Snapshot (span.End.GetContainingLine().LineNumber)

    /// Change the line number on the range by the given count
    let ChangeEndLine range count =
        let makeLines (tss:ITextSnapshot) startLine endLine =
            let endLine = min (endLine+count) (tss.LineCount-1)
            let endLine = max endLine 0
            let startLine = min startLine endLine
            Lines(tss, startLine, endLine)
        match range with 
        | Range.Lines(tss,startLine,endLine) -> makeLines tss startLine endLine
        | Range.RawSpan(span) ->
            let startLine = span.Start.GetContainingLine().LineNumber
            let endLine = span.End.GetContainingLine().LineNumber
            makeLines span.Snapshot startLine endLine
        | Range.SingleLine(line) -> 
            let num = line.LineNumber + count
            let num = min num (line.Snapshot.LineCount-1)
            let num = max num 0
            SingleLine(line.Snapshot.GetLineFromLineNumber(num))

    /// Combine the two ranges
    let CombineRanges left right = 
        let getStartLine range =
            match range with
            | Range.Lines(tss,startLine,_) -> (tss,Some(startLine))
            | Range.SingleLine(line) -> (line.Snapshot,Some(line.LineNumber))
            | Range.RawSpan(span) -> (span.Snapshot,None)
        let getEndLine range =
            match range with
            | Range.Lines(_,_,endLine) -> Some(endLine)
            | Range.SingleLine(line) -> Some(line.LineNumber)
            | Range.RawSpan(_) -> None
        let tss,startLine = getStartLine left
        let endLine = getEndLine right
        match startLine,endLine with
        | Some(startLine),Some(endLine) ->  Range.Lines(tss, startLine, endLine)
        | _ -> 
            let left = GetSnapshotSpan left
            let right = GetSnapshotSpan right
            let span = new SnapshotSpan(left.Start, right.End)
            Range.RawSpan(span)

    /// Parse out a number from the input string
    let ParseNumber (input:char list) =

        // Parse out the input into the list of digits and remaining input
        let rec getDigits (input:char list) =
            let inner (head:char) tail = 
                if System.Char.IsDigit head then 
                    let restDigits,restInput = getDigits tail
                    (head :: restDigits, restInput)
                else ([],input)
            ListUtil.tryProcessHead input inner (fun() -> ([],input))
            
        let digits,remaining = getDigits input
        let numberStr = 
            digits 
                |> Seq.ofList
                |> Array.ofSeq
                |> StringUtil.ofCharArray
        let mutable number = 0
        match System.Int32.TryParse(numberStr, &number) with
        | false -> (None,input)
        | true -> (Some(number), remaining)

    /// Parse out a line number 
    let private ParseLineNumber (tss:ITextSnapshot) (input:char list) =
    
        let opt,remaining = ParseNumber input
        match opt with 
        | Some(number) ->
            let number = TssUtil.VimLineToTssLine number
            if number < tss.LineCount then 
                let line = tss.GetLineFromLineNumber(number)
                let range = Range.SingleLine(line)
                ValidRange(range, LineNumber, remaining)
            else
                let msg = sprintf "Invalid Range: Line Number %d is not a valid number in the file" number
                Error(msg)
        | None -> Error("Expected a line number")

    /// Parse out a mark 
    let private ParseMark (point:SnapshotPoint) (map:IMarkMap) (list:char list) = 
        let inner head tail = 
            let opt = map.GetMark point.Snapshot.TextBuffer head
            match opt with 
            | Some(point) -> 
                let line = point.Position.GetContainingLine()
                ValidRange(Range.SingleLine(line), Mark, tail)
            | None -> Error Resources.Range_MarkNotValidInFile
        ListUtil.tryProcessHead list inner (fun () -> Error Resources.Range_MarkMissingIdentifier)

    /// Parse out a single item in the range.
    let private ParseItem (point:SnapshotPoint) (map:IMarkMap) (list:char list) =
        let head = list |> List.head 
        if CharUtil.IsDigit head then
            ParseLineNumber point.Snapshot list
        else if head = '.' then
            let line = point.GetContainingLine().LineNumber
            let range = Range.Lines(point.Snapshot, line,line)
            ValidRange(range,CurrentLine, list |> List.tail)
        else if head = '\'' then
            ParseMark point map (list |> List.tail)
        else
            NoRange

    let private ParsePlusMinus range (list:char list) =
        let getCount list =
            let opt,list = ParseNumber list
            match opt with 
            | Some(num) -> num,list
            | None -> 1,list
        let inner head tail = 
            if head = '+' then 
                let count,tail = getCount tail
                (ChangeEndLine range count,tail)
            elif head = '-' then
                let count,tail = getCount tail
                (ChangeEndLine range (-count),tail)
            else 
                range,list
        ListUtil.tryProcessHead list inner (fun () -> range,list)

    let private ParseRangeCore (point:SnapshotPoint) (map:IMarkMap) (originalInput:char list) =
        _parser {
            let! range,kind,remaining = ParseItem point map originalInput
            let range,remaining = ParsePlusMinus range remaining
            match ListUtil.tryHead remaining with
            | None -> return! Succeeded(range, remaining)
            | Some (head,tail) ->
                if head = ',' then 
                    let! rightRange,_,remaining = ParseItem point map tail
                    let rightRange,remaining = ParsePlusMinus rightRange remaining
                    let fullRange = CombineRanges range rightRange
                    return! Succeeded(fullRange, remaining)
                else if head = ';' then 
                    let point = (GetSnapshotSpan range).Start
                    let! rightRange,_,remaining = ParseItem point map tail
                    let rightRange,remaining = ParsePlusMinus rightRange remaining
                    let fullRange = CombineRanges range rightRange
                    return! Succeeded(fullRange, remaining)
                else if kind = LineNumber then
                    return! Succeeded(range, remaining)
                else if kind = CurrentLine then
                    return! Succeeded(range, remaining)
                else
                    return! Failed Resources.Range_ConnectionMissing
        }

    let ParseRange (point:SnapshotPoint) (map:IMarkMap) (list:char list) = 
        let inner head tail =
            if head = '%' then 
                let tss = point.Snapshot
                let span = new SnapshotSpan(tss, 0, tss.Length)
                ParseRangeResult.Succeeded(RawSpan(span), tail)
            else
                ParseRangeCore point map list
        ListUtil.tryProcessHead list inner (fun() -> ParseRangeResult.NoRange)
