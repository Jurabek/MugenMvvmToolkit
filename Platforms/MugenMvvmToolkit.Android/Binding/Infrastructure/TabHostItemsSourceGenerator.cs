#region Copyright

// ****************************************************************************
// <copyright file="TabHostItemsSourceGenerator.cs">
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
using System.Collections;
using System.Collections.Generic;
using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using JetBrains.Annotations;
using MugenMvvmToolkit.Binding.Interfaces;
using MugenMvvmToolkit.Binding.Interfaces.Models;
using MugenMvvmToolkit.DataConstants;
using MugenMvvmToolkit.Interfaces.Models;
using MugenMvvmToolkit.Interfaces.ViewModels;
using MugenMvvmToolkit.Interfaces.Views;
using MugenMvvmToolkit.Models.EventArg;
using Object = Java.Lang.Object;

namespace MugenMvvmToolkit.Binding.Infrastructure
{
    internal sealed class TabHostItemsSourceGenerator : ItemsSourceGeneratorBase
    {
        #region Nested types

        private sealed class TabFactory : Object, TabHost.ITabContentFactory
        {
            #region Fields

            private readonly TabHostItemsSourceGenerator _generator;

            #endregion

            #region Constructors

            public TabFactory([NotNull] TabHostItemsSourceGenerator generator)
            {
                Should.NotBeNull(generator, "generator");
                _generator = generator;
            }

            #endregion

            #region Implementation of ITabContentFactory

            public View CreateTabContent(string tag)
            {
                var view = new View(_generator.TabHost.Context);
                view.SetMinimumWidth(0);
                view.SetMinimumHeight(0);
                return view;
            }

            #endregion
        }

        private sealed class EmptyTemplateSelector : IDataTemplateSelector
        {
            #region Fields

            public static readonly EmptyTemplateSelector Instance;
            public static readonly View EmptyView;

            #endregion

            #region Constructors

            static EmptyTemplateSelector()
            {
                Instance = new EmptyTemplateSelector();
                EmptyView = new View(Application.Context);
            }

            private EmptyTemplateSelector()
            {
            }

            #endregion

            #region Implementation of IDataTemplateSelector

            public object SelectTemplate(object item, object container)
            {
                return EmptyView;
            }

            #endregion
        }

        internal sealed class TabInfo
        {
            #region Fields

            public readonly object Item;
            public readonly TabHost.TabSpec TabSpec;
            public readonly object Content;

            #endregion

            #region Constructors

            public TabInfo(object item, TabHost.TabSpec tabSpec, object content)
            {
                Item = item;
                TabSpec = tabSpec;
                Content = content;
            }

            #endregion
        }

        #endregion

        #region Fields

        private const string SelectedTabIndexKey = "~@tindex";
        private bool _isRestored;

        private object _currentTabContent;
        private readonly TabFactory _tabFactory;
        private readonly Dictionary<string, TabInfo> _tabToContent;
        private readonly IBindingMemberInfo _selectedItemMember;
        private bool _ingoreTabChanged;

        private readonly DataTemplateProvider _itemTemplateProvider;
        private readonly DataTemplateProvider _contentTemplateProvider;

        internal readonly TabHost TabHost;

        #endregion

        #region Constructors

        static TabHostItemsSourceGenerator()
        {
            TabChangedDelegate = TabChanged;
            RemoveTabDelegate = RemoveTab;
        }

        private TabHostItemsSourceGenerator([NotNull] TabHost tabHost)
        {
            Should.NotBeNull(tabHost, "tabHost");
            TabHost = tabHost;
            TabHost.Setup();
            _tabToContent = new Dictionary<string, TabInfo>();
            _tabFactory = new TabFactory(this);
            _itemTemplateProvider = new DataTemplateProvider(tabHost, AttachedMemberConstants.ItemTemplate,
                AttachedMemberConstants.ItemTemplateSelector);
            _contentTemplateProvider = new DataTemplateProvider(tabHost, AttachedMemberConstants.ContentTemplate,
                AttachedMemberConstants.ContentTemplateSelector);
            _selectedItemMember = BindingServiceProvider
                                                 .MemberProvider
                                                 .GetBindingMember(tabHost.GetType(), AttachedMemberConstants.SelectedItem, false, false);
            TryListenActivity(tabHost.Context);
            TabHost.TabChanged += TabHostOnTabChanged;
        }

        #endregion

        #region Overrides of ItemsSourceGeneratorBase

        protected override bool IsTargetDisposed
        {
            get { return !TabHost.IsAlive(); }
        }

        protected override void Update(IEnumerable itemsSource, IDataContext context = null)
        {
            base.Update(itemsSource, context);
            if (!_isRestored)
            {
                _isRestored = true;
                TryRestoreSelectedIndex();
            }
        }

        protected override IEnumerable ItemsSource { get; set; }

        protected override void Add(int insertionIndex, int count)
        {
            if (insertionIndex == TabHost.TabWidget.TabCount)
            {
                for (int i = 0; i < count; i++)
                    TabHost.AddTab(CreateTabSpec(insertionIndex + i));
            }
            else
                Refresh();
        }

        protected override void Remove(int removalIndex, int count)
        {
            Refresh();
        }

        protected override void Replace(int startIndex, int count)
        {
            Refresh();
        }

        protected override void Refresh()
        {
            try
            {
                _ingoreTabChanged = true;
                string selectedTag = TabHost.CurrentTabTag;
                var oldValues = new Dictionary<string, TabInfo>(_tabToContent);
                TabHost.CurrentTab = 0;
                TabHost.ClearAllTabs();
                _tabToContent.Clear();

                int count = ItemsSource.Count();
                TabInfo firstTab = null;
                for (int i = 0; i < count; i++)
                {
                    var tabInfo = TryRecreateTabInfo(i, oldValues);
                    TabHost.AddTab(tabInfo.TabSpec);
                    if (i == 0)
                        firstTab = tabInfo;
                }
                foreach (var oldValue in oldValues)
                    RemoveTabDelegate(this, oldValue.Value);


                _ingoreTabChanged = false;
                if (count == 0)
                    OnEmptyTab();
                else
                {
                    if (selectedTag == null || !_tabToContent.ContainsKey(selectedTag))
                        selectedTag = firstTab.TabSpec.Tag;
                    TabHost.SetCurrentTabByTag(selectedTag);
                    OnTabChanged(selectedTag);
                }
            }
            finally
            {
                _ingoreTabChanged = false;
            }
        }

        #endregion

        #region Properties

        [NotNull]
        public static Action<TabHostItemsSourceGenerator, object, object, bool, bool> TabChangedDelegate { get; set; }

        [NotNull]
        public static Action<TabHostItemsSourceGenerator, TabInfo> RemoveTabDelegate { get; set; }

        #endregion

        #region Methods

        public static IItemsSourceGenerator GetOrAdd(TabHost tabHost)
        {
            return ServiceProvider.AttachedValueProvider.GetOrAdd(tabHost, Key,
                (host, o) => new TabHostItemsSourceGenerator(host), null);
        }

        public void SetSelectedItem(object selectedItem, IDataContext context = null)
        {
            if (selectedItem == null)
            {
                TabHost.CurrentTab = 0;
                if (TabHost.CurrentTabTag != null)
                    OnTabChanged(TabHost.CurrentTabTag);
            }
            else
            {
                foreach (var pair in _tabToContent)
                {
                    if (pair.Value.Item == selectedItem)
                    {
                        if (TabHost.CurrentTabTag != pair.Key)
                            TabHost.SetCurrentTabByTag(pair.Key);
                        break;
                    }
                }
            }
        }

        private void TabHostOnTabChanged(object sender, TabHost.TabChangeEventArgs args)
        {
            OnTabChanged(args.TabId);
        }

        private void OnTabChanged(string id)
        {
            if (_ingoreTabChanged)
                return;
            var oldValue = _currentTabContent;
            TabInfo info;
            if (_tabToContent.TryGetValue(id, out info))
            {
                _currentTabContent = info.Content;
                if (ReferenceEquals(_currentTabContent, oldValue))
                    return;

                TabChangedDelegate(this, oldValue, _currentTabContent, true, true);
                if (_selectedItemMember != null)
                    _selectedItemMember.SetValue(TabHost, new[] { info.Item });
            }
            else
            {
                _currentTabContent = TabHost.CurrentTabView;
                if (_selectedItemMember != null)
                    _selectedItemMember.SetValue(TabHost, new object[] { TabHost.CurrentTabView });
            }
        }

        private void OnEmptyTab()
        {
            if (_selectedItemMember != null)
                _selectedItemMember.SetValue(TabHost, BindingExtensions.NullValue);
            if (TabHost.TabContentView != null)
                TabHost.TabContentView.RemoveAllViews();
        }

        private TabHost.TabSpec CreateTabSpec(int index)
        {
            return CreateTabInfo(GetItem(index)).TabSpec;
        }

        private TabInfo TryRecreateTabInfo(int index, Dictionary<string, TabInfo> oldValues)
        {
            object item = GetItem(index);
            foreach (var tabInfo in oldValues)
            {
                if (Equals(tabInfo.Value.Item, item))
                {
                    oldValues.Remove(tabInfo.Key);
                    _tabToContent[tabInfo.Key] = tabInfo.Value;
                    return tabInfo.Value;
                }
            }
            return CreateTabInfo(item);
        }

        private TabInfo CreateTabInfo(object item)
        {
            string id = Guid.NewGuid().ToString("n");
            var spec = TabHost.NewTabSpec(id);
            var tabInfo = new TabInfo(item, spec, GetContent(item));
            _tabToContent[id] = tabInfo;
            SetIndicator(spec, item);
            spec.SetContent(_tabFactory);
            BindingServiceProvider.ContextManager.GetBindingContext(spec).Value = item;
            return tabInfo;
        }

        private void SetIndicator(TabHost.TabSpec tabSpec, object item)
        {
            var viewModel = item as IViewModel;
            if (viewModel != null)
                viewModel.Settings.Metadata.AddOrUpdate(ViewModelConstants.StateNotNeeded, true);

            var templateId = _itemTemplateProvider.GetTemplateId();
            var selector = _itemTemplateProvider.GetDataTemplateSelector();
            if (templateId == null && selector == null)
                selector = EmptyTemplateSelector.Instance;
            object content = PlatformExtensions.GetContentView(TabHost, TabHost.Context, item, templateId, selector);
            if (content == EmptyTemplateSelector.EmptyView)
            {
                content = null;
                if (viewModel is IHasDisplayName)
                    BindingServiceProvider.BindingProvider.CreateBindingsFromString(tabSpec, "Title DisplayName", null);
                else
                    tabSpec.SetIndicator(item.ToStringSafe("(null)"));
            }
            var view = content as View;
            if (view == null)
                tabSpec.SetIndicator(content.ToStringSafe("(null)"));
            else
                tabSpec.SetIndicator(view);
        }

        private object GetContent(object item)
        {
            return PlatformExtensions.GetContentView(TabHost, TabHost.Context,
                item, _contentTemplateProvider.GetTemplateId(), _contentTemplateProvider.GetDataTemplateSelector());
        }

        private void TryRestoreSelectedIndex()
        {
            var activityView = TabHost.Context as IActivityView;
            if (activityView == null)
                return;
            var bundle = activityView.Mediator.Bundle;
            if (bundle != null)
            {
                var i = bundle.GetInt(SelectedTabIndexKey, int.MinValue);
                if (i != int.MinValue)
                    TabHost.CurrentTab = i;
            }
            activityView.Mediator.SaveInstanceState += ActivityViewOnSaveInstanceState;
        }

        private void ActivityViewOnSaveInstanceState(Activity sender, ValueEventArgs<Bundle> args)
        {
            var index = TabHost.CurrentTab;
            if (index > 0)
                args.Value.PutInt(SelectedTabIndexKey, index);
        }

        private static void TabChanged(TabHostItemsSourceGenerator generator, object oldValue, object newValue, bool clearOldValue, bool setNewValue)
        {
            if (clearOldValue)
            {
                var view = oldValue as View;
                if (view != null)
                    generator.TabHost.TabContentView.RemoveView(view);
            }
            if (setNewValue)
            {
                var view = newValue as View;
                if (view == null)
                    generator.TabHost.TabContentView.RemoveAllViews();
                else
                    generator.TabHost.TabContentView.AddView(view);
            }
        }

        private static void RemoveTab(TabHostItemsSourceGenerator generator, TabInfo tab)
        {
            var view = tab.Content as View;
            if (view != null)
                view.ClearBindingsHierarchically(true, true);
        }

        #endregion
    }
}