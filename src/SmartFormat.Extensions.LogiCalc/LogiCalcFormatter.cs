using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SmartFormat.Core.Extensions;
using SmartFormat.Core.Formatting;
using SmartFormat.Core.Parsing;
using SmartFormat.Core.Settings;
using SmartFormat.Utilities;

namespace SmartFormat.Extensions;

/// <summary>
/// An <see cref="IFormatter"/> used to evaluate mathematical expressions.
/// See https://github.com/ncalc/ncalc and
/// https://www.codeproject.com/Articles/18880/State-of-the-Art-Expression-Evaluation
/// </summary>
/// <example>
/// var data = new { Arg1 = 3, Arg2 = 4 };
/// _ = Smart.Format("{:calc(0.00):({Arg1} + {Arg2}) * 5}");
/// result: 35.00
/// </example>
/// <remarks>
/// The <see cref="LogiCalcFormatter"/> will use plain <see cref="Placeholder"/>s as NCalc parameters.
/// NCalc parameters are useful when a value is unknown at compile time,
/// or when performance is important and repetitive parsing and compilation of the expression tree
/// can be saved for further calculations.
/// </remarks>
public class LogiCalcFormatter : IFormatter
{
    private readonly Dictionary<string, object?> _parameters = new(50);
    private Format? _nCalcFormat;
    [ThreadStatic] // creates isolated versions of the Expression in each thread
    private static NCalc.Expression? _nCalcExpression;
    [ThreadStatic] // creates isolated versions of the StringBuilder in each thread
    private static StringBuilder? _sb;

    ///<inheritdoc/>
    public string Name { get; set; } = "calc";

    ///<inheritdoc/>
    public bool CanAutoDetect { get; set; } = false;

    /// <summary>
    /// Contains the <see cref="NCalc.Handlers.ParameterArgs"/> created from the plain <see cref="Placeholder"/>s
    /// when the <see cref="LogiCalcFormatter"/> was invoked last.
    /// Parameters are cleared and re-created for each new format string.
    /// </summary>
    public IReadOnlyDictionary<string, object?> NCalcParameters => _parameters;

    /// <summary>
    /// Gets or sets the <see cref="NCalc.Handlers.EvaluateFunctionHandler"/>.
    /// </summary>
    public NCalc.Handlers.EvaluateFunctionHandler? EvaluateFunction { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="NCalc.Handlers.EvaluateParameterHandler"/>.
    /// </summary>
    public NCalc.Handlers.EvaluateParameterHandler? EvaluateParameter { get; set; }

    ///<inheritdoc />
    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        var format = formattingInfo.Format;
        if (format == null) return false;
        
        var fi = (FormattingInfo) formattingInfo;
        
        var nCalcOptions = formattingInfo.FormatDetails.Settings.CaseSensitivity == CaseSensitivityType.CaseInsensitive
            ? NCalc.ExpressionOptions.IgnoreCase
            : NCalc.ExpressionOptions.None;
        
        _parameters.Clear();
        var expressionValue = CreateNCalcExpression(fi, _parameters);

        _nCalcExpression = new NCalc.Expression(expressionValue, nCalcOptions, CultureInfo.InvariantCulture)
        {
            Parameters = _parameters
        };
        
        try
        {
            if (EvaluateFunction != null) _nCalcExpression.EvaluateFunction += EvaluateFunction;
            if (EvaluateParameter != null) _nCalcExpression.EvaluateParameter += EvaluateParameter;

            var result = _nCalcExpression.Evaluate();

            // Create the Format if it doesn't exist,
            // or recreate the Format if Alignment or FormatterOptions have changed
            if (_nCalcFormat?.Items[0] is not Placeholder ph || ph.Alignment != formattingInfo.Alignment ||
                ph.FormatterOptions != formattingInfo.FormatterOptions)
            {
                // Creating a tailored Format for this specific case causes code duplication
                // and is only about 2.5% faster, so we use the standard Parser
                _nCalcFormat =
                    formattingInfo.FormatDetails.Formatter.Parser.ParseFormat(
                        $"{{,{formattingInfo.Alignment}:{formattingInfo.FormatterOptions}}}");
            }

            formattingInfo.FormatAsChild(_nCalcFormat, result);
        }
        catch (Exception exception)
        {
            throw new FormattingException(format, exception, format.StartIndex);
        }
        
        return true;
    }

    private static string CreateNCalcExpression(FormattingInfo fi, Dictionary<string, object?> parameters)
    {
        _sb ??= new StringBuilder(fi.Format!.Length);
        _sb.Clear();
        _sb.Capacity = fi.Format!.Length; // default sb.Capacity is only 16
        
        foreach (var item in fi.Format.Items)
        {
            if (item is LiteralText literalItem)
            {
                _sb.Append(literalItem);
                continue;
            }

            // Otherwise, the item must be a placeholder.
            var placeholder = (Placeholder) item;

            // It's not a plain Placeholder like "{DateTime.Now.Month}",
            // so we cannot use it as an NCalc parameter but use the value directly
            if (!placeholder.IsPlainPlaceholder)
            {
                var result = fi.Format(CultureInfo.InvariantCulture, placeholder, fi.CurrentValue);
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
                _sb.Append(result);
#else
                _sb.Append(result.ToString());
#endif
                continue;
            }

            // Use the Placeholder's selector names as the NCalc parameter
            // Note 1: The name of the "{}" placeholder is the empty string
            // Note 2: The selector names are joined using "dot notation", e.g. "{Person.Siblings[0]}" => "Person.Siblings.0"
            var pName = placeholder.GetSelectorsAsSpanDotNotation().ToString();
            // NCalc does not allow empty or blank as parameter name,
            // so we use a replacement that is not a valid C# identifier
            pName = pName.Length == 0 ? "." : pName;
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
            parameters.TryAdd(pName, fi.GetValue(placeholder));
#else
            if (!parameters.ContainsKey(pName))
            {
                parameters.Add(pName, fi.GetValue(placeholder));
            }
#endif
            // Format as NCalc parameter
            // Parameters are useful when a value is unknown at compile time,
            // or when performance is important (NCalc parsing can be saved for further calculations).
            _sb.Append($"[{pName}]");            
        }

        return _sb.ToString();
    }
}
