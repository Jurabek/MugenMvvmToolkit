﻿#region Copyright

// ****************************************************************************
// <copyright file="PlatformWrapperRegistrationModule.cs">
// Copyright (c) 2012-2015 Vyacheslav Volkov
// </copyright>
// ****************************************************************************
// <author>Vyacheslav Volkov</author>
// <email>vvs0205@outlook.com</email>
// <project>MugenMvvmToolkit</project>
// <web>https://github.com/MugenMvvmToolkit/MugenMvvmToolkit</web>
// <license>
// See license.txt in this solution or http://opensource.org/licenses/MS-PL
// </license>
// ****************************************************************************

#endregion

using System;
using System.ComponentModel;
using System.Windows.Controls;
using MugenMvvmToolkit.Infrastructure;
using MugenMvvmToolkit.Interfaces.Views;

namespace MugenMvvmToolkit.Modules
{
    public class PlatformWrapperRegistrationModule : WrapperRegistrationModuleBase
    {
        #region Nested types

        private sealed class WindowViewWrapper : IWindowView, IViewWrapper
        {
            #region Fields

            private readonly ChildWindow _window;

            #endregion

            #region Constructors

            public WindowViewWrapper(ChildWindow window)
            {
                Should.NotBeNull(window, "window");
                _window = window;
            }

            #endregion

            #region Implementation of IWindowView

            public Type ViewType
            {
                get { return _window.GetType(); }
            }

            public object View
            {
                get { return _window; }
            }

            public void Show()
            {
                _window.Show();
            }

            public void Close()
            {
                _window.Close();
            }

            event EventHandler<CancelEventArgs> IWindowView.Closing
            {
                add { _window.Closing += value; }
                remove { _window.Closing -= value; }
            }

            #endregion
        }

        #endregion

        #region Overrides of WrapperRegistrationModuleBase

        /// <summary>
        ///     Registers the wrappers using <see cref="WrapperManager" /> class.
        /// </summary>
        protected override void RegisterWrappers(WrapperManager wrapperManager)
        {
            wrapperManager.AddWrapper<IWindowView, WindowViewWrapper>(
                (type, context) => typeof (ChildWindow).IsAssignableFrom(type),
                (o, context) => new WindowViewWrapper((ChildWindow) o));
        }

        #endregion
    }
}