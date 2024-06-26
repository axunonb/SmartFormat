// 
// Copyright SmartFormat Project maintainers and contributors.
// Licensed under the MIT license.

using System;
using System.Threading;
using SmartFormat.Core.Parsing;
using SmartFormat.Core.Settings;
using SmartFormat.Pooling.SpecializedPools;

namespace SmartFormat.Pooling.SmartPools;

/// <summary>
/// The object pool for <see cref="LiteralText"/>.
/// </summary>
internal sealed class LiteralTextPool : SmartPoolAbstract<LiteralText>
{
    private static readonly Lazy<LiteralTextPool> Lazy = new(() => new LiteralTextPool(),
        SmartSettings.IsThreadSafeMode
            ? LazyThreadSafetyMode.PublicationOnly
            : LazyThreadSafetyMode.None);
        
    /// <summary>
    /// CTOR.
    /// </summary>
    /// <remarks>
    /// <see cref="SpecializedPoolAbstract{T}.Policy"/> must be set before initializing the pool
    /// </remarks>
    private LiteralTextPool()
    {
        Policy.FunctionOnCreate = () => new LiteralText();
        Policy.ActionOnReturn = lt => lt.Clear();
    }

    /// <inheritdoc/>
    public override void Return(LiteralText toReturn)
    {
        if (ReferenceEquals(toReturn, InitializationObject.LiteralText)) throw new PoolingException($"{nameof(InitializationObject)}s cannot be returned to the pool.", GetType());
        base.Return(toReturn);
    }

    /// <summary>
    /// Gets the existing instance of the pool or lazy-creates a new one, which is then added to the registry.
    /// </summary>
    public static LiteralTextPool Instance => PoolRegistry.GetOrAdd(Lazy.Value);
}
