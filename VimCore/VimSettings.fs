﻿#light

namespace Vim
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Vim.GlobalSettingNames
open Vim.LocalSettingNames

type internal SettingsMap
    (
        _rawData : (string*string*SettingKind*SettingValue) seq,
        _isGlobal : bool ) =

    let _settingChangedEvent = new Event<_>()

    /// Create the settings off of the default map
    let mutable _settings =
         _rawData
         |> Seq.map (fun (name,abbrev,kind,value) -> { Name=name; Abbreviation=abbrev;Kind=kind; DefaultValue=value;Value=value; IsGlobal=_isGlobal})
         |> Seq.map (fun setting -> (setting.Name,setting))
         |> Map.ofSeq

    member x.AllSettings = _settings |> Map.toSeq |> Seq.map (fun (_,value) -> value)
    member x.OwnsSetting settingName = x.GetSetting settingName |> Option.isSome
    member x.SettingChanged = _settingChangedEvent.Publish

    /// Replace a Setting with a new value
    member x.ReplaceSetting settingName setting = 
        _settings <- _settings |> Map.add settingName setting
        _settingChangedEvent.Trigger setting

    member x.TrySetValue settingNameOrAbbrev value =

        /// Determine if the value and the kind are compatible
        let doesValueMatchKind kind = 
            match kind,value with
            | (NumberKind,NumberValue(_)) -> true
            | (StringKind,StringValue(_)) -> true
            | (ToggleKind,ToggleValue(_)) -> true
            | (_, NoValue) -> true
            | _ -> false

        match x.GetSetting settingNameOrAbbrev with
        | None -> false
        | Some(setting) ->
            if doesValueMatchKind setting.Kind then
                let setting = { setting with Value=value }
                _settings <- _settings |> Map.add setting.Name setting
                _settingChangedEvent.Trigger setting
                true
            else false

    member x.TrySetValueFromString settingNameOrAbbrev strValue = 
        match x.GetSetting settingNameOrAbbrev with
        | None -> false
        | Some(setting) ->
            match x.ConvertStringToValue strValue setting.Kind with
            | None -> false
            | Some(value) -> x.TrySetValue setting.Name value

    member x.GetSetting settingName = 
        match _settings |> Map.tryFind settingName with
        | Some(s) -> Some(s)
        | None -> 
            _settings 
            |> Map.toSeq 
            |> Seq.map (fun (_,value) -> value) 
            |> Seq.tryFind (fun setting -> setting.Abbreviation = settingName)
            

    /// Get a boolean setting value.  Will throw if the setting name does not exist
    member x.GetBoolValue settingName = 
        let setting = _settings |> Map.find settingName
        match setting.Value.AggregateValue,setting.DefaultValue.AggregateValue with 
        | ToggleValue(b),_ -> b
        | NoValue,ToggleValue(b) -> b
        | _ -> failwith "Invalid"

    /// Get a string setting value.  Will throw if the setting name does not exist
    member x.GetStringValue settingName =
        let setting = _settings |> Map.find settingName
        match setting.Value.AggregateValue,setting.DefaultValue.AggregateValue with
        | StringValue(s),_ -> s
        | NoValue,StringValue(s) -> s
        | _ -> failwith "Invalid"

    /// Get a number setting value.  Will throw if the setting name does not exist
    member x.GetNumberValue settingName =
        let setting = _settings |> Map.find settingName
        match setting.Value.AggregateValue,setting.DefaultValue.AggregateValue with
        | NumberValue(n),_ -> n
        | NoValue,NumberValue(n) -> n
        | _ -> failwith "Invalid"

    member x.ConvertStringToValue str kind =
        
        let convertToNumber() = 
            let ret,value = System.Int32.TryParse str
            if ret then Some (NumberValue(value)) else None
        let convertToBoolean() =
            let ret,value = System.Boolean.TryParse str
            if ret then Some (ToggleValue(value)) else None
        match kind with
        | NumberKind -> convertToNumber()
        | ToggleKind -> convertToBoolean()
        | StringKind -> Some (StringValue(str))

type internal GlobalSettings() =

    static let DisableCommandLet = KeyInputUtil.VimKeyAndModifiersToKeyInput VimKey.F12 (KeyModifiers.Control ||| KeyModifiers.Shift)

    static let GlobalSettings = 
        [|
            ( CaretOpacityName, CaretOpacityName, NumberKind, NumberValue(65) );
            ( HighlightSearchName, "hls", ToggleKind, ToggleValue(false) );
            ( IgnoreCaseName,"ic", ToggleKind, ToggleValue(false) );
            ( MagicName, MagicName, ToggleKind, ToggleValue(true) );
            ( ShiftWidthName, "sw", NumberKind, NumberValue(4) );
            ( SelectionName, "sel", StringKind, StringValue("inclusive"));
            ( ScrollOffsetName, "so", NumberKind, NumberValue(0) );
            ( SmartCaseName, "scs", ToggleKind, ToggleValue(false) );
            ( StartOfLineName, "sol", ToggleKind, ToggleValue(true) );
            ( TabStopName, "ts", NumberKind, NumberValue(8) );
            ( TildeOpName, "top", ToggleKind, ToggleValue(false) );
            ( VimRcName, VimRcName, StringKind, StringValue(System.String.Empty) );
            ( VimRcPathsName, VimRcPathsName, StringKind, StringValue(System.String.Empty) );
            ( VirtualEditName, "ve", StringKind, StringValue(StringUtil.empty));
            ( VisualBellName, "vb", ToggleKind, ToggleValue(false) );
            ( CaretOpacityName, CaretOpacityName, NumberKind, NumberValue(65) );
        |]

    let _map = SettingsMap(GlobalSettings, true)

    static member DisableCommand = DisableCommandLet

    interface IVimGlobalSettings with
        // IVimSettings

        member x.AllSettings = _map.AllSettings
        member x.TrySetValue settingName value = _map.TrySetValue settingName value
        member x.TrySetValueFromString settingName strValue = _map.TrySetValueFromString settingName strValue
        member x.GetSetting settingName = _map.GetSetting settingName

        // IVimGlobalSettings 
        member x.CaretOpacity
            with get() = _map.GetNumberValue CaretOpacityName
            and set value = _map.TrySetValue CaretOpacityName (NumberValue(value)) |> ignore
        member x.HighlightSearch
            with get() = _map.GetBoolValue HighlightSearchName
            and set value = _map.TrySetValue HighlightSearchName (ToggleValue(value)) |> ignore
        member x.IgnoreCase
            with get()  = _map.GetBoolValue IgnoreCaseName
            and set value = _map.TrySetValue IgnoreCaseName (ToggleValue(value)) |> ignore
        member x.Magic
            with get() = _map.GetBoolValue MagicName
            and set value = _map.TrySetValue MagicName (ToggleValue(value)) |> ignore
        member x.ScrollOffset
            with get() = _map.GetNumberValue ScrollOffsetName
            and set value = _map.TrySetValue ScrollOffsetName (NumberValue(value)) |> ignore
        member x.Selection
            with get() = _map.GetStringValue SelectionName
            and set value = _map.TrySetValue SelectionName (StringValue(value)) |> ignore
        member x.ShiftWidth  
            with get() = _map.GetNumberValue ShiftWidthName
            and set value = _map.TrySetValue ShiftWidthName (NumberValue(value)) |> ignore
        member x.SmartCase
            with get() = _map.GetBoolValue SmartCaseName
            and set value = _map.TrySetValue SmartCaseName (ToggleValue(value)) |> ignore
        member x.StartOfLine 
            with get() = _map.GetBoolValue StartOfLineName
            and set value = _map.TrySetValue StartOfLineName (ToggleValue(value)) |> ignore
        member x.TabStop
            with get() = _map.GetNumberValue TabStopName
            and set value = _map.TrySetValue TabStopName (NumberValue(value)) |> ignore
        member x.TildeOp
            with get() = _map.GetBoolValue TildeOpName
            and set value = _map.TrySetValue TildeOpName (ToggleValue(value)) |> ignore
        member x.VimRc 
            with get() = _map.GetStringValue VimRcName
            and set value = _map.TrySetValue VimRcName (StringValue(value)) |> ignore
        member x.VimRcPaths 
            with get() = _map.GetStringValue VimRcPathsName
            and set value = _map.TrySetValue VimRcPathsName (StringValue(value)) |> ignore
        member x.VirtualEdit
            with get() = _map.GetStringValue VirtualEditName
            and set value = _map.TrySetValue VirtualEditName (StringValue(value)) |> ignore
        member x.VisualBell
            with get() = _map.GetBoolValue VisualBellName
            and set value = _map.TrySetValue VisualBellName (ToggleValue(value)) |> ignore
        member x.DisableCommand = DisableCommandLet
        member x.IsVirtualEditOneMore = 
            let value = _map.GetStringValue VirtualEditName
            StringUtil.split ',' value |> Seq.exists (fun x -> StringUtil.isEqual "onemore" x)

        [<CLIEvent>]
        member x.SettingChanged = _map.SettingChanged

type internal LocalSettings
    ( 
        _global : IVimGlobalSettings,
        _textView : ITextView ) as this =
    
    static let LocalSettings =
        [|
            ( CursorLineName, "cul", ToggleKind, ToggleValue(false) )
            ( NumberName, "nu", ToggleKind, ToggleValue(false) )
            ( ScrollName, "scr", NumberKind, NumberValue(25) )
            ( QuoteEscapeName, "qe", StringKind, StringValue(@"\") )
        |]

    let _map = SettingsMap(LocalSettings, false)

    do
        let setting = _map.GetSetting ScrollName |> Option.get
        _map.ReplaceSetting ScrollName {setting with Value=NoValue;DefaultValue=CalculatedValue(this.CalculateScroll) }

    /// Calculate the scroll value as specified in the Vim documenation.  Should be half the number of 
    /// visible lines 
    member private x.CalculateScroll() =
        let defaultValue = 10
        let lineCount = 
            try
                let col = _textView.TextViewLines
                match col.FirstVisibleLine,col.LastVisibleLine with
                | (null,_) -> defaultValue
                | (_,null) -> defaultValue
                | (top,bottom) ->
                    let topLine = top.Start.GetContainingLine()
                    let endLine = bottom.End.GetContainingLine()
                    (endLine.LineNumber - topLine.LineNumber) / 2
            with 
                // This will be thrown if we're currently in the middle of an inner layout
                :? System.InvalidOperationException -> defaultValue
        NumberValue(lineCount)
    
    interface IVimLocalSettings with 
        // IVimSettings
        
        member x.AllSettings = _map.AllSettings |> Seq.append _global.AllSettings
        member x.TrySetValue settingName value = 
            if _map.OwnsSetting settingName then _map.TrySetValue settingName value
            else _global.TrySetValue settingName value
        member x.TrySetValueFromString settingName strValue = 
            if _map.OwnsSetting settingName then _map.TrySetValueFromString settingName strValue
            else _global.TrySetValueFromString settingName strValue
        member x.GetSetting settingName =
            if _map.OwnsSetting settingName then _map.GetSetting settingName
            else _global.GetSetting settingName

        member x.GlobalSettings = _global
        member x.CursorLine 
            with get() = _map.GetBoolValue CursorLineName
            and set value = _map.TrySetValue CursorLineName (ToggleValue(value)) |> ignore
        member x.Scroll 
            with get() = _map.GetNumberValue ScrollName
            and set value = _map.TrySetValue ScrollName (NumberValue(value)) |> ignore
        member x.QuoteEscape
            with get() = _map.GetStringValue QuoteEscapeName
            and set value = _map.TrySetValue QuoteEscapeName (StringValue(value)) |> ignore

        [<CLIEvent>]
        member x.SettingChanged = _map.SettingChanged
    

