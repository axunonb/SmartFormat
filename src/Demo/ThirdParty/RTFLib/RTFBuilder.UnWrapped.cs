﻿#nullable disable

using System;

namespace Demo.ThirdParty.RTFLib;

partial class RTFBuilder
{
    #region Nested type: RTFBuilderUnWrapped

    // ----------------------------------------------------------------------------------------
    //    _                ___        _..-._   Date: 12/11/08    23:38
    //    \`.|\..----...-'`   `-._.-'' _.-..'     
    //    /  ' `         ,       __.-'' 
    //    )/` _/     \   `-_,   /     Solution: RTFLib
    //    `-'" `"\_  ,_.-;_.-\_ ',    Project : RTFLib                                 
    //        _.-'_./   {_.'   ; /    Author  : Anton
    //       {_.-``-'         {_/     Assembly: 1.0.0.0
    //                                Copyright © 2005-2008, Rogue Trader/MWM
    //        Project Item Name:      RTFBuilder.UnWrapped.cs - Code
    //        Purpose:                Cancels persistent Formatting Changes on an unwrapped RtfBuilder
    // ----------------------------------------------------------------------------------------
    /// <summary>
    /// Cancels persistent Formatting Changes on an unwrapped RtfBuilder
    /// Exposed by the FormatLock on RtfBuilderbase
    /// </summary>
    private class RTFBuilderUnWrapped : IDisposable
    {
        #region Fields

        private readonly RTFBuilder _builder;
        private readonly RTFFormatWrap wrapped;

        #endregion

        #region Constructor

        public RTFBuilderUnWrapped(RTFBuilder builder)
        {
            wrapped = new RTFFormatWrap(builder);
            _builder = builder;
            _builder._unwrapped = true;
        }

        #endregion

        #region Override Methods

        ~RTFBuilderUnWrapped()
        {
            Dispose(false);
        }

        #endregion

        #region Public Methods

        public void Dispose(bool disposing)
        {
            if (_builder != null)
            {
                wrapped.Dispose();
                _builder._unwrapped = false;
            }
            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }

    #endregion
}
