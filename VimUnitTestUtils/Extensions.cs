﻿using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using NUnit.Framework;
using Vim.Modes;
using Vim.Modes.Command;

namespace Vim.UnitTest
{
    public static class Extensions
    {
        #region CountResult

        internal static CountResult.NeedMore AsNeedMore(this CountResult res)
        {
            return (CountResult.NeedMore)res;
        }

        #endregion

        #region ProcessResult

        public static ProcessResult.SwitchMode AsSwitchMode(this ProcessResult res)
        {
            return (ProcessResult.SwitchMode)res;
        }

        public static ProcessResult.SwitchModeWithArgument AsSwitchModeWithArgument(this ProcessResult res)
        {
            return (ProcessResult.SwitchModeWithArgument)res;
        }

        #endregion

        #region MotionResult


        public static MotionResult.Complete AsComplete(this MotionResult res)
        {
            Assert.IsTrue(res.IsComplete);
            return (MotionResult.Complete)res;
        }

        #endregion

        #region ModeUtil.Result

        public static Result.Failed AsFailed(this Result res)
        {
            return (Result.Failed)res;
        }

        #endregion

        #region VisualSpan

        public static VisualSpan.Multiple AsMultiple(this VisualSpan span)
        {
            return (VisualSpan.Multiple)span;
        }

        public static VisualSpan.Single AsSingle(this VisualSpan span)
        {
            return (VisualSpan.Single)span;
        }

        #endregion

        #region ParseRangeResult

        internal static ParseRangeResult.Succeeded AsSucceeded(this ParseRangeResult res)
        {
            return (ParseRangeResult.Succeeded)res;
        }

        internal static ParseRangeResult.Failed AsFailed(this ParseRangeResult res)
        {
            return (ParseRangeResult.Failed)res;
        }

        #endregion

        #region IKeyMap

        public static IEnumerable<KeyInput> GetKeyMapping(this IKeyMap keyMap, KeyInput ki, KeyRemapMode mode)
        {
            var set = KeyInputSet.NewOneKeyInput(ki);
            return keyMap.GetKeyMapping(set, mode).AsMapped().Item.KeyInputs;
        }

        public static KeyMappingResult GetKeyMappingResult(this IKeyMap keyMap, KeyInput ki, KeyRemapMode mode)
        {
            var set = KeyInputSet.NewOneKeyInput(ki);
            return keyMap.GetKeyMapping(set, mode);
        }

        #endregion

        #region KeyMappingResult

        public static KeyMappingResult.Mapped AsMapped(this KeyMappingResult res)
        {
            Assert.IsTrue(res.IsMapped);
            return (KeyMappingResult.Mapped)res;
        }

        public static KeyMappingResult.RecursiveMapping AsRecursiveMapping(this KeyMappingResult res)
        {
            Assert.IsTrue(res.IsRecursiveMapping);
            return (KeyMappingResult.RecursiveMapping)res;
        }

        #endregion

        #region SearchText

        public static SearchText.Pattern AsPattern(this SearchText text)
        {
            Assert.IsTrue(text.IsPattern);
            return (SearchText.Pattern)text;
        }

        public static SearchText.StraightText AsStraightText(this SearchText text)
        {
            Assert.IsTrue(text.IsStraightText);
            return (SearchText.StraightText)text;
        }

        public static SearchText.WholeWord AsWholeWord(this SearchText text)
        {
            Assert.IsTrue(text.IsWholeWord);
            return (SearchText.WholeWord)text;
        }

        #endregion

        #region RepeatableChange

        public static RepeatableChange.TextChange AsTextChange(this RepeatableChange change)
        {
            Assert.IsTrue(change.IsTextChange);
            return (RepeatableChange.TextChange)change;
        }

        public static RepeatableChange.CommandChange AsCommandChange(this RepeatableChange change)
        {
            Assert.IsTrue(change.IsCommandChange);
            return (RepeatableChange.CommandChange)change;
        }

        #endregion

        #region SettingValue

        public static SettingValue.StringValue AsStringValue(this SettingValue value)
        {
            Assert.IsTrue(value.IsStringValue);
            return (SettingValue.StringValue)value;
        }

        public static SettingValue.ToggleValue AsBooleanValue(this SettingValue value)
        {
            Assert.IsTrue(value.IsToggleValue);
            return (SettingValue.ToggleValue)value;
        }

        public static SettingValue.NumberValue AsNumberValue(this SettingValue value)
        {
            Assert.IsTrue(value.IsNumberValue);
            return (SettingValue.NumberValue)value;
        }

        #endregion

        #region Range

        internal static Range.Lines AsLines(this Range range)
        {
            return (Range.Lines)range;
        }

        internal static Range.RawSpan AsRawSpan(this Range range)
        {
            return (Range.RawSpan)range;
        }

        internal static Range.SingleLine AsSingleLine(this Range range)
        {
            return (Range.SingleLine)range;
        }

        #endregion

        #region RunKeyInputResult

        public static RunKeyInputResult.CommandRan AsCommandRan(this RunKeyInputResult result)
        {
            return (RunKeyInputResult.CommandRan)result;
        }

        public static RunKeyInputResult.CommandErrored AsCommandErrored(this RunKeyInputResult result)
        {
            return (RunKeyInputResult.CommandErrored)result;
        }

        #endregion

        #region IMode

        public static bool CanProcess(this IMode mode, VimKey key)
        {
            return mode.CanProcess(KeyInputUtil.VimKeyToKeyInput(key));
        }

        public static ProcessResult Process(this IMode mode, VimKey key)
        {
            return mode.Process(KeyInputUtil.VimKeyToKeyInput(key));
        }

        public static ProcessResult Process(this IMode mode, char c)
        {
            return mode.Process((KeyInputUtil.CharToKeyInput(c)));
        }

        public static ProcessResult Process(this IMode mode, string input)
        {
            ProcessResult last = null;
            foreach (var c in input)
            {
                var i = KeyInputUtil.CharToKeyInput(c);
                last = mode.Process(c);
            }

            return last;
        }

        #endregion

        #region IVimBuffer

        public static bool Process(this IVimBuffer buf, VimKey key)
        {
            return buf.Process(KeyInputUtil.VimKeyToKeyInput(key));
        }

        public static bool Process(this IVimBuffer buf, char c)
        {
            return buf.Process(KeyInputUtil.CharToKeyInput(c));
        }

        public static void Process(this IVimBuffer buf, string input)
        {
            foreach (var c in input)
            {
                var i = KeyInputUtil.CharToKeyInput(c);
                buf.Process(i);
            }
        }

        public static Register GetRegister(this IVimBuffer buffer, char c)
        {
            var name = RegisterNameUtil.CharToRegister(c).Value;
            return buffer.RegisterMap.GetRegister(name);
        }

        #endregion

        #region ITextView

        public static SnapshotPoint GetPoint(this ITextView textView, int position)
        {
            return new SnapshotPoint(textView.TextSnapshot, position);
        }

        public static ITextSnapshotLine GetLine(this ITextView textView, int line)
        {
            return textView.TextSnapshot.GetLineFromLineNumber(line);
        }

        public static SnapshotSpan GetLineSpanIncludingLineBreak(this ITextView textView, int startLine, int endLine = -1)
        {
            endLine = endLine >= 0 ? endLine : startLine;
            return textView.TextSnapshot.GetLineSpanIncludingLineBreak(startLine, endLine);
        }

        public static SnapshotSpan GetLineSpan(this ITextView textView, int startLine, int endLine = -1)
        {
            endLine = endLine >= 0 ? endLine : startLine;
            return textView.TextSnapshot.GetLineSpan(startLine, endLine);
        }

        public static CaretPosition MoveCaretTo(this ITextView textView, int position)
        {
            return textView.Caret.MoveTo(new SnapshotPoint(textView.TextSnapshot, position));
        }

        public static void SelectAndUpdateCaret(this ITextView textView, SnapshotSpan span, TextSelectionMode mode = TextSelectionMode.Stream)
        {
            textView.Selection.Mode = mode;
            textView.Selection.Select(span, false);
            MoveCaretTo(textView, span.End.Position);
        }

        public static ITextSnapshotLine GetCaretLine(this ITextView textView)
        {
            return textView.Caret.Position.BufferPosition.GetContainingLine();
        }

        #endregion

        #region ITextBuffer

        public static ITextSnapshotLine GetLineFromLineNumber(this ITextBuffer buffer, int line)
        {
            return buffer.CurrentSnapshot.GetLineFromLineNumber(line);
        }

        public static ITextSnapshotLine GetLine(this ITextBuffer buffer, int line)
        {
            return buffer.CurrentSnapshot.GetLineFromLineNumber(line);
        }

        public static SnapshotSpan GetLineSpanIncludingLineBreak(this ITextBuffer buffer, int startLine, int endLine = -1)
        {
            endLine = endLine >= 0 ? endLine : startLine;
            return buffer.CurrentSnapshot.GetLineSpanIncludingLineBreak(startLine, endLine);
        }

        public static SnapshotSpan GetLineSpan(this ITextBuffer buffer, int startLine, int endLine = -1)
        {
            endLine = endLine >= 0 ? endLine : startLine;
            return buffer.CurrentSnapshot.GetLineSpan(startLine, endLine);
        }

        public static SnapshotPoint GetPoint(this ITextBuffer buffer, int position)
        {
            return new SnapshotPoint(buffer.CurrentSnapshot, position);
        }

        public static SnapshotPoint GetEndPoint(this ITextBuffer buffer)
        {
            return buffer.CurrentSnapshot.GetEndPoint();
        }

        public static SnapshotSpan GetSpan(this ITextBuffer buffer, int start, int length)
        {
            return buffer.CurrentSnapshot.GetSpan(start, length);
        }

        public static void SetText(this ITextBuffer buffer, string text)
        {
            buffer.Replace(new Span(0, buffer.CurrentSnapshot.Length), text);
        }

        #endregion

        #region ITextSnapshot



        public static ITextSnapshotLine GetLine(this ITextSnapshot tss, int lineNumber)
        {
            return tss.GetLineFromLineNumber(lineNumber);
        }

        public static SnapshotSpan GetLineSpan(this ITextSnapshot tss, int startLine, int endLine = -1)
        {
            endLine = endLine >= 0 ? endLine : startLine;
            var start = tss.GetLineFromLineNumber(startLine);
            var end = tss.GetLineFromLineNumber(endLine);
            return new SnapshotSpan(start.Start, end.End);
        }

        public static SnapshotSpan GetLineSpanIncludingLineBreak(this ITextSnapshot tss, int startLine, int endLine = -1)
        {
            endLine = endLine >= 0 ? endLine : startLine;
            var start = tss.GetLineFromLineNumber(startLine);
            var end = tss.GetLineFromLineNumber(endLine);
            return new SnapshotSpan(start.Start, end.EndIncludingLineBreak);
        }

        public static SnapshotPoint GetPoint(this ITextSnapshot tss, int position)
        {
            return new SnapshotPoint(tss, position);
        }

        public static SnapshotPoint GetEndPoint(this ITextSnapshot tss)
        {
            return new SnapshotPoint(tss, tss.Length);
        }

        public static SnapshotSpan GetSpan(this ITextSnapshot tss, int start, int length)
        {
            return new SnapshotSpan(tss, start, length);
        }

        #endregion

        #region ITextSnapshotPoint

        public static SnapshotSpan GetSpan(this SnapshotPoint point, int length)
        {
            return new SnapshotSpan(point, length);
        }

        #endregion

        #region ICommandRunner

        public static RunKeyInputResult Run(this ICommandRunner runner, char c)
        {
            return runner.Run(KeyInputUtil.CharToKeyInput(c));
        }

        #endregion

        #region CommandRunnerState

        public static CommandRunnerState.NotFinishWithCommand AsNotFinishedWithCommand(this CommandRunnerState state)
        {
            return (CommandRunnerState.NotFinishWithCommand)state;
        }

        public static CommandRunnerState.NotEnoughMatchingPrefix AsNotEnoughMatchingPrefix(this CommandRunnerState state)
        {
            return (CommandRunnerState.NotEnoughMatchingPrefix)state;
        }

        #endregion

        #region Dispatcher

        public static void DoEvents(this System.Windows.Threading.Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            Action<DispatcherFrame> action = _ => { frame.Continue = false; };
            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                action,
                frame);
            Dispatcher.PushFrame(frame);
        }

        #endregion

        public static SnapshotSpan GetSpan(this ITextSelection selection)
        {
            var span = new SnapshotSpan(selection.Start.Position, selection.End.Position);
            return span;
        }

        public static void UpdateValue(this Register reg, string value)
        {
            var data = StringData.NewSimple(value);
            var regValue = new RegisterValue(data, MotionKind.Inclusive, OperationKind.CharacterWise);
            reg.Value = regValue;
        }

        public static SnapshotPoint GetCaretPoint(this ITextView view)
        {
            return view.Caret.Position.BufferPosition;
        }

        public static Register GetRegister(this IRegisterMap map, char c)
        {
            var name = RegisterNameUtil.CharToRegister(c).Value;
            return map.GetRegister(name);
        }

    }
}
