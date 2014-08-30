﻿#region Copyright
// ****************************************************************************
// <copyright file="BindingErrorProvider.cs">
// Copyright © Vyacheslav Volkov 2012-2014
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
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using JetBrains.Annotations;
using MugenMvvmToolkit.Binding.Interfaces;

namespace MugenMvvmToolkit.Infrastructure
{
    /// <summary>
    ///     Represents the class that provides a user interface for indicating that a control on a form has an error associated
    ///     with it using the <see cref="ErrorProvider" /> component.
    /// </summary>
    public class BindingErrorProvider : IBindingErrorProvider
    {
        #region Fields

        private const string ErrorProviderName = "#_b_e_p";

        #endregion

        #region Implementation of IBindingErrorProvider

        /// <summary>
        ///     Sets errors for binding target.
        /// </summary>
        /// <param name="target">The target object.</param>
        /// <param name="errors">The collection of errors</param>
        public void SetErrors(object target, IList<object> errors)
        {
            var control = target as Control;
            if (control == null || AttachedMembersModule.DisableValidationMember.GetValue(control, null))
                return;
            var rootControl = PlatformExtensions.GetRootControl(control);
            if (rootControl == null)
                return;
            var errorProvider = GetErrorProvider(rootControl);
            if (errorProvider != null)
                SetErrors(control, errorProvider, errors);
        }

        #endregion

        #region Methods

        /// <summary>
        ///     Sets errors for control.
        /// </summary>
        protected virtual void SetErrors([NotNull] Control target, [NotNull] ErrorProvider errorProvider,
            [NotNull] IList<object> errors)
        {
            errorProvider.SetError(target, errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
        }

        /// <summary>
        /// Gets an <see cref="ErrorProvider"/> for the specified root <see cref="Control"/>.
        /// </summary>
        [CanBeNull]
        protected virtual ErrorProvider GetErrorProvider(Control rootControl)
        {
            ErrorProvider errorProvider;
            Control errorControl = rootControl.Controls.Find(ErrorProviderName, true).FirstOrDefault();
            if (errorControl == null)
            {
                errorProvider = new ErrorProvider
                {
                    ContainerControl = (ContainerControl)rootControl.GetContainerControl()
                };
                var control = new Control
                {
                    Visible = false,
                    Name = ErrorProviderName,
                    Width = 0,
                    Height = 0,
                    Tag = errorProvider
                };
                rootControl.Controls.Add(control);
            }
            else
                errorProvider = (ErrorProvider)errorControl.Tag;
            return errorProvider;
        }

        #endregion
    }
}