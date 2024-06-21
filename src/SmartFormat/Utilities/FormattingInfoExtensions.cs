//
// Copyright SmartFormat Project maintainers and contributors.
// Licensed under the MIT license.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SmartFormat.Core.Formatting;
using SmartFormat.Core.Output;
using SmartFormat.Core.Parsing;
using SmartFormat.Pooling.SmartPools;
using SmartFormat.Pooling.SpecializedPools;
using SmartFormat.ZString;

namespace SmartFormat.Utilities;

/// <summary>
/// This class contains extension methods for <see cref="FormattingInfo"/>.
/// </summary>
public static class FormattingInfoExtensions
{
    /// <summary>
    /// Gets the value for the given <paramref name="placeholder"/> from the registered <see cref="Core.Extensions.ISource"/>s.
    /// </summary>
    /// <param name="formattingInfo"></param>
    /// <param name="placeholder"></param>
    /// <returns>The value for the given <paramref name="placeholder"/> from the registered <see cref="Core.Extensions.ISource"/>s.</returns>
    public static object? GetValue(this FormattingInfo formattingInfo, Placeholder placeholder)
    {
        if (placeholder.Selectors.Count == 0) return formattingInfo.CurrentValue;

        var fi = formattingInfo.CreateChild(placeholder);
        fi.FormatDetails.Formatter.EvaluateSelectors(fi);

        return fi.CurrentValue;
    }

    /// <summary>
    /// Outputs the formatted value of the given <paramref name="placeholder"/> and <paramref name="value"/>.
    /// </summary>
    /// <param name="formattingInfo"></param>
    /// <param name="provider"></param>
    /// <param name="placeholder"></param>
    /// <param name="value"></param>
    /// <returns>The formatted value of the given <paramref name="placeholder"/> and <paramref name="value"/>.</returns>
    public static ReadOnlySpan<char> Format(this FormattingInfo formattingInfo,
        IFormatProvider? provider, Placeholder placeholder,
        object? value)
    {
        using var fmtObject = FormatPool.Instance.Get(out var format);

        format.Initialize(formattingInfo.FormatDetails.Settings, placeholder.BaseString);
        format.Items.Add(placeholder);

        using var zsOutput = new ZStringOutput(ZStringBuilderUtilities.CalcCapacity(format));
        formattingInfo.FormatDetails.Formatter.FormatInto(zsOutput, provider, format, value);
        return zsOutput.Output.AsSpan();
    }

    // Todo: Refactor and test this method
    internal static void GetAllValues(this FormattingInfo rootFormattingInfo, Format? format,
        Dictionary<string, (Exception?, object?)> result)
    {
        if (rootFormattingInfo.Format is null || format is null) return;

        foreach (var item in rootFormattingInfo.Format.Items)
        {
            if (item is LiteralText) continue;
            
            // Otherwise, the item must be a placeholder.
            var placeholder = (Placeholder) item;

            var childFormattingInfo = rootFormattingInfo.CreateChild(placeholder);
            
            try
            {
                // Note: If there is no selector (like {:0.00}),
                // FormattingInfo.CurrentValue is left unchanged
                ValueTuple<Exception?, object?> evalResult;
                try
                {
                    rootFormattingInfo.FormatDetails.Formatter.EvaluateSelectors(childFormattingInfo);
                    evalResult = (null, childFormattingInfo.Result);

                    /*
                    using var fdObject = FormatDetailsPool.Instance.Get(out var formatDetails);
                    // We don't add the nestedFormattingInfo as child to the parent, so we have to dispose it
                    using var fiObject = FormattingInfoPool.Instance.Get(out var nestedFormattingInfo);
                    formatDetails.Initialize(rootFormattingInfo.FormatDetails.Formatter, format,
                        rootFormattingInfo.FormatDetails.OriginalArgs,
                        CultureInfo.InvariantCulture, null!);
                    nestedFormattingInfo.Initialize(null, formatDetails, format, rootFormattingInfo.CurrentValue);

                    nestedFormattingInfo.FormatDetails.Formatter.EvaluateSelectors(childFormattingInfo); // ??
                    */
                }
                catch (Exception e)
                {
                    evalResult = (e, null);
                }

                var selector = GetSelectorsAsString(childFormattingInfo);
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
                result.TryAdd(selector, evalResult);
#else
                if (!result.ContainsKey(selector)) result.Add(selector, evalResult);
#endif

                // If the format has nested placeholders, we process those, too.
                // E.g.: "{2:list:{:{FirstName}}|, }"
                if (placeholder.Format is { HasNested: true })
                {
                    // Recursive call
                    GetAllValues(childFormattingInfo, placeholder.Format, result);
                }
            }
            catch (Exception ex)
            {
                // An error occurred while evaluation selectors
                var errorIndex = placeholder.Format?.StartIndex ??
                                 placeholder.Selectors[placeholder.Selectors.Count - 1].EndIndex;
                rootFormattingInfo.FormatDetails.Formatter.FormatError(item, ex, errorIndex, childFormattingInfo);
            }
        }
    }

    private static string GetSelectorsAsString(FormattingInfo formattingInfo)
    {
        var parentFormattingInfo = formattingInfo;
        var hasParent = false;
        while (parentFormattingInfo.Parent is { Placeholder: not null } &&
               parentFormattingInfo.Parent.Placeholder.Selectors.Count != 0)
        {
            parentFormattingInfo = parentFormattingInfo.Parent;
            hasParent = true;
        }

        // Selector coming from list formatter:
        // if value is part of the parent value, we can determine the index here

        var selectors = new List<Selector>();
        if (hasParent)
            selectors.AddRange(
                parentFormattingInfo.Placeholder!.Selectors.Where(s => s.Length > 0 && s.Operator != ","));

        selectors.AddRange(formattingInfo.Placeholder!.Selectors.Where(s => s.Length > 0 && s.Operator != ","));
        return string.Join(formattingInfo.FormatDetails.Settings.Parser.SelectorOperator.ToString(), selectors);
    }
}
