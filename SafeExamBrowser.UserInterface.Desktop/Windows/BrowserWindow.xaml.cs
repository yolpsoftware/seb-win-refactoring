﻿/*
 * Copyright (c) 2021 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SafeExamBrowser.Core.Contracts.Resources.Icons;
using SafeExamBrowser.I18n.Contracts;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.Settings.Browser;
using SafeExamBrowser.UserInterface.Contracts;
using SafeExamBrowser.UserInterface.Contracts.Browser;
using SafeExamBrowser.UserInterface.Contracts.Browser.Data;
using SafeExamBrowser.UserInterface.Contracts.Browser.Events;
using SafeExamBrowser.UserInterface.Contracts.Events;
using SafeExamBrowser.UserInterface.Contracts.Windows;
using SafeExamBrowser.UserInterface.Contracts.Windows.Events;
using SafeExamBrowser.UserInterface.Desktop.Controls.Browser;
using SafeExamBrowser.UserInterface.Shared.Utilities;

namespace SafeExamBrowser.UserInterface.Desktop.Windows
{
	internal partial class BrowserWindow : Window, IBrowserWindow
	{
		IModuleLogger logger;
		private bool isMainWindow;
		private BrowserSettings settings;
		private IText text;
		private WindowClosingEventHandler closing;
		private bool browserControlGetsFocusFromTaskbar = false;
		private IInputElement tabKeyDownFocusElement = null;

		private WindowSettings WindowSettings
		{
			get { return isMainWindow ? settings.MainWindow : settings.AdditionalWindow; }
		}

		public bool CanNavigateBackwards { set => Dispatcher.Invoke(() => BackwardButton.IsEnabled = value); }
		public bool CanNavigateForwards { set => Dispatcher.Invoke(() => ForwardButton.IsEnabled = value); }
		public IntPtr Handle { get; private set; }

		public event AddressChangedEventHandler AddressChanged;
		public event ActionRequestedEventHandler BackwardNavigationRequested;
		public event ActionRequestedEventHandler DeveloperConsoleRequested;
		public event FindRequestedEventHandler FindRequested;
		public event ActionRequestedEventHandler ForwardNavigationRequested;
		public event ActionRequestedEventHandler HomeNavigationRequested;
		public event ActionRequestedEventHandler ReloadRequested;
		public event ActionRequestedEventHandler ZoomInRequested;
		public event ActionRequestedEventHandler ZoomOutRequested;
		public event ActionRequestedEventHandler ZoomResetRequested;
		public event LoseFocusRequestedEventHandler LoseFocusRequested;

		event WindowClosingEventHandler IWindow.Closing
		{
			add { closing += value; }
			remove { closing -= value; }
		}

		internal BrowserWindow(IBrowserControl browserControl, BrowserSettings settings, bool isMainWindow, IText text) //, IModuleLogger logger)
		{
			this.isMainWindow = isMainWindow;
			this.settings = settings;
			this.text = text;
			//this.logger = logger;

			InitializeComponent();
			InitializeBrowserWindow(browserControl);
		}

		public void BringToForeground()
		{
			Dispatcher.Invoke(() =>
			{
				if (WindowState == WindowState.Minimized)
				{
					WindowState = WindowState.Normal;
				}

				Activate();
			});
		}

		public new void Close()
		{
			Dispatcher.Invoke(() =>
			{
				Closing -= BrowserWindow_Closing;
				closing?.Invoke();
				base.Close();
			});
		}

		public new void Hide()
		{
			Dispatcher.Invoke(base.Hide);
		}

		public new void Show()
		{
			Dispatcher.Invoke(base.Show);
		}

		public void ShowFindbar()
		{
			Dispatcher.InvokeAsync(() =>
			{
				Findbar.Visibility = Visibility.Visible;
				FindTextBox.Focus();
			});
		}

		public void UpdateAddress(string url)
		{
			Dispatcher.Invoke(() => UrlTextBox.Text = url);
		}

		public void UpdateIcon(IconResource icon)
		{
			Dispatcher.InvokeAsync(() =>
			{
				if (icon is BitmapIconResource bitmap)
				{
					Icon = new BitmapImage(bitmap.Uri);
				}
			});
		}

		public void UpdateDownloadState(DownloadItemState state)
		{
			Dispatcher.InvokeAsync(() =>
			{
				var isNewItem = true;

				foreach (var child in Downloads.Children)
				{
					if (child is DownloadItemControl control && control.Id == state.Id)
					{
						control.Update(state);
						isNewItem = false;

						break;
					}
				}

				if (isNewItem)
				{
					var control = new DownloadItemControl(state.Id, text);

					control.Update(state);
					Downloads.Children.Add(control);
				}

				DownloadsButton.Visibility = Visibility.Visible;
				DownloadsPopup.IsOpen = IsActive;
			});
		}

		public void UpdateLoadingState(bool isLoading)
		{
			Dispatcher.Invoke(() => ProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Hidden);
		}

		public void UpdateProgress(double value)
		{
			Dispatcher.Invoke(() => ProgressBar.Value = value * 100);
		}

		public void UpdateTitle(string title)
		{
			Dispatcher.Invoke(() => Title = title);
		}

		public void UpdateZoomLevel(double value)
		{
			Dispatcher.Invoke(() => ZoomLevel.Text = $"{value}%");
		}

		private void BrowserWindow_Closing(object sender, CancelEventArgs e)
		{
			if (isMainWindow)
			{
				e.Cancel = true;
			}
			else
			{
				closing?.Invoke();
			}
		}

		private void BrowserWindow_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Tab)
			{
				var hasShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
				if (Toolbar.IsKeyboardFocusWithin && hasShift)
				{
					var firstActiveElementInToolbar = Toolbar.PredictFocus(FocusNavigationDirection.Right);
					if (firstActiveElementInToolbar is System.Windows.UIElement)
					{
						var control = firstActiveElementInToolbar as System.Windows.UIElement;
						if (control.IsKeyboardFocusWithin)
						{
							this.LoseFocusRequested?.Invoke(false);
							e.Handled = true;
						}
					}
				}

				tabKeyDownFocusElement = FocusManager.GetFocusedElement(this);
			}
			else
			{
				tabKeyDownFocusElement = null;
			}
		}

		private void BrowserWindow_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.F5)
			{
				ReloadRequested?.Invoke();
			}

			if (e.Key == Key.Home)
			{
				HomeNavigationRequested?.Invoke();
			}

			if (settings.AllowFind && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.F)
			{
				ShowFindbar();
			}

			if (e.Key == Key.Tab)
			{
				var hasCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
				var hasShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
				if (BrowserControlHost.IsFocused && hasCtrl)
				{
					if (Findbar.Visibility == Visibility.Hidden || hasShift)
					{
						Toolbar.Focus();
					}
					else if (Toolbar.Visibility == Visibility.Hidden)
					{
						Findbar.Focus();
					}
				}
				else if (MenuPopup.IsKeyboardFocusWithin)
				{
					var focusedElement = FocusManager.GetFocusedElement(this);
					var focusedControl = focusedElement as System.Windows.Controls.Control;
					var prevFocusedControl = tabKeyDownFocusElement as System.Windows.Controls.Control;
					if (focusedControl != null && prevFocusedControl != null)
					{
						//var commonAncestor = focusedControl.FindCommonVisualAncestor(prevFocusedControl);
						//var nextTab = GetNextTab(MenuPopup, this, true);
						if (!hasShift && focusedControl.TabIndex < prevFocusedControl.TabIndex)
						{
							MenuPopup.IsOpen = false;
							FocusBrowser();
						}
						else if (hasShift && focusedControl.TabIndex > prevFocusedControl.TabIndex)
						{
							MenuPopup.IsOpen = false;
							MenuButton.Focus();
						}
					}
				}
			}
		}

		/// <summary>
		/// Get next tab order element. Copied from https://stackoverflow.com/questions/5756448/in-wpf-how-can-i-get-the-next-control-in-the-tab-order
		/// </summary>
		/// <param name="e">The element to get next tab order</param>
		/// <param name="container">The container element owning 'e'. Make sure this is a container of 'e'.</param>
		/// <param name="goDownOnly">True if search only itself and inside of 'container'; otherwise false.
		/// If true and next tab order element is outside of 'container', result in null.</param>
		/// <returns>Next tab order element or null if not found</returns>
		public DependencyObject GetNextTab(DependencyObject e, DependencyObject container, bool goDownOnly)
		{
			var navigation = typeof(FrameworkElement)
				.GetProperty("KeyboardNavigation", BindingFlags.NonPublic | BindingFlags.Static)
				.GetValue(null);

			var method = navigation
				.GetType()
				.GetMethod("GetNextTab", BindingFlags.NonPublic | BindingFlags.Instance);

			return method.Invoke(navigation, new object[] { e, container, goDownOnly }) as DependencyObject;
		}

		private void BrowserWindow_Loaded(object sender, RoutedEventArgs e)
		{
			Handle = new WindowInteropHelper(this).Handle;

			if (isMainWindow)
			{
				this.DisableCloseButton();
			}
		}

		private void FindbarCloseButton_Click(object sender, RoutedEventArgs e)
		{
			FindRequested?.Invoke("thisisahacktoclearthesearchresultsasitappearsthatthereisnosuchfunctionalityincef", true, false);
			Findbar.Visibility = Visibility.Collapsed;
		}

		private void FindNextButton_Click(object sender, RoutedEventArgs e)
		{
			FindRequested?.Invoke(FindTextBox.Text, false, FindCaseSensitiveCheckBox.IsChecked == true);
		}

		private void FindPreviousButton_Click(object sender, RoutedEventArgs e)
		{
			FindRequested?.Invoke(FindTextBox.Text, false, FindCaseSensitiveCheckBox.IsChecked == true, false);
		}

		private void FindTextBox_KeyUp(object sender, KeyEventArgs e)
		{
			if (string.IsNullOrEmpty(FindTextBox.Text))
			{
				FindRequested?.Invoke("thisisahacktoclearthesearchresultsasitappearsthatthereisnosuchfunctionalityincef", true, false);
			}
			else
			{
				FindRequested?.Invoke(FindTextBox.Text, true, FindCaseSensitiveCheckBox.IsChecked == true);
			}
		}

		private CustomPopupPlacement[] Popup_PlacementCallback(Size popupSize, Size targetSize, Point offset)
		{
			return new[]
			{
				new CustomPopupPlacement(new Point(targetSize.Width - Toolbar.Margin.Right - popupSize.Width, -2), PopupPrimaryAxis.None)
			};
		}

		private void SystemParameters_StaticPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(SystemParameters.WorkArea))
			{
				Dispatcher.InvokeAsync(InitializeBounds);
			}
		}

		private void UrlTextBox_GotMouseCapture(object sender, MouseEventArgs e)
		{
			if (UrlTextBox.Tag as bool? != true)
			{
				UrlTextBox.SelectAll();
				UrlTextBox.Tag = true;
			}
		}

		private void UrlTextBox_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				AddressChanged?.Invoke(UrlTextBox.Text);
			}
		}

		private void InitializeBrowserWindow(IBrowserControl browserControl)
		{
			if (browserControl is System.Windows.Forms.Control control)
			{
				BrowserControlHost.Child = control;
			}

			RegisterEvents();
			InitializeBounds();
			ApplySettings();
			LoadIcons();
			LoadText();
		}

		private void RegisterEvents()
		{
			BackwardButton.Click += (o, args) => BackwardNavigationRequested?.Invoke();
			Closing += BrowserWindow_Closing;
			DeveloperConsoleButton.Click += (o, args) => DeveloperConsoleRequested?.Invoke();
			DownloadsButton.Click += (o, args) => DownloadsPopup.IsOpen = !DownloadsPopup.IsOpen;
			DownloadsButton.MouseLeave += (o, args) => Task.Delay(250).ContinueWith(_ => Dispatcher.Invoke(() => DownloadsPopup.IsOpen = DownloadsPopup.IsMouseOver));
			DownloadsPopup.CustomPopupPlacementCallback = new CustomPopupPlacementCallback(Popup_PlacementCallback);
			DownloadsPopup.MouseLeave += (o, args) => Task.Delay(250).ContinueWith(_ => Dispatcher.Invoke(() => DownloadsPopup.IsOpen = DownloadsPopup.IsMouseOver));
			FindbarCloseButton.Click += FindbarCloseButton_Click;
			FindNextButton.Click += FindNextButton_Click;
			FindPreviousButton.Click += FindPreviousButton_Click;
			FindMenuButton.Click += (o, args) => ShowFindbar();
			FindTextBox.KeyUp += FindTextBox_KeyUp;
			ForwardButton.Click += (o, args) => ForwardNavigationRequested?.Invoke();
			HomeButton.Click += (o, args) => HomeNavigationRequested?.Invoke();
			Loaded += BrowserWindow_Loaded;
			MenuButton.Click += MenuButton_Click;
			MenuButton.MouseLeave += (o, args) => Task.Delay(250).ContinueWith(_ => Dispatcher.Invoke(() => MenuPopup.IsOpen = MenuPopup.IsMouseOver));
			MenuPopup.CustomPopupPlacementCallback = new CustomPopupPlacementCallback(Popup_PlacementCallback);
			MenuPopup.MouseLeave += (o, args) => Task.Delay(250).ContinueWith(_ => Dispatcher.Invoke(() => MenuPopup.IsOpen = MenuPopup.IsMouseOver));
			KeyDown += BrowserWindow_KeyDown;
			KeyUp += BrowserWindow_KeyUp;
			LocationChanged += (o, args) => { DownloadsPopup.IsOpen = false; MenuPopup.IsOpen = false; };
			ReloadButton.Click += (o, args) => ReloadRequested?.Invoke();
			SizeChanged += (o, args) => { DownloadsPopup.IsOpen = false; MenuPopup.IsOpen = false; };
			SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
			UrlTextBox.GotKeyboardFocus += (o, args) => UrlTextBox.SelectAll();
			UrlTextBox.GotMouseCapture += UrlTextBox_GotMouseCapture;
			UrlTextBox.LostKeyboardFocus += (o, args) => UrlTextBox.Tag = null;
			UrlTextBox.LostFocus += (o, args) => UrlTextBox.Tag = null;
			UrlTextBox.KeyUp += UrlTextBox_KeyUp;
			UrlTextBox.MouseDoubleClick += (o, args) => UrlTextBox.SelectAll();
			ZoomInButton.Click += (o, args) => ZoomInRequested?.Invoke();
			ZoomOutButton.Click += (o, args) => ZoomOutRequested?.Invoke();
			ZoomResetButton.Click += (o, args) => ZoomResetRequested?.Invoke();
			BrowserControlHost.GotKeyboardFocus += BrowserControlHost_GotKeyboardFocus;
		}

		private void MenuButton_Click(object sender, RoutedEventArgs e)
		{
			MenuPopup.IsOpen = !MenuPopup.IsOpen;
			ZoomInButton.Focus();
		}

		private void BrowserControlHost_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
		{
			var forward = !this.browserControlGetsFocusFromTaskbar;

			// focus the first / last element on the page
			var javascript = @"
if (typeof __SEB_focusElement === 'undefined') {
  __SEB_focusElement = function (forward) {
    var items = [].map
      .call(document.body.querySelectorAll(['input', 'select', 'a[href]', 'textarea', 'button', '[tabindex]']), function(el, i) { return { el, i } })
      .filter(function(e) { return e.el.tabIndex >= 0 && !e.el.disabled && e.el.offsetParent; })
      .sort(function(a,b) { return a.el.tabIndex === b.el.tabIndex ? a.i - b.i : (a.el.tabIndex || 9E9) - (b.el.tabIndex || 9E9); })
    var item = items[forward ? 1 : items.length - 1];
    setTimeout(function () { item.focus(); }, 20);
  }
}";
			var control = BrowserControlHost.Child as IBrowserControl;
			control.ExecuteJavascript(javascript, result =>
			{
				if (!result.Success)
				{
					System.Windows.Forms.MessageBox.Show(result.Message);
				}
			});

			control.ExecuteJavascript("__SEB_focusElement(" + forward.ToString().ToLower() + ")", result =>
			{
				if (!result.Success)
				{
					System.Windows.Forms.MessageBox.Show(result.Message);
				}
			});
		}

		private void ApplySettings()
		{
			BackwardButton.IsEnabled = true; // WindowSettings.AllowBackwardNavigation;
			BackwardButton.Visibility = Visibility.Visible; // WindowSettings.AllowBackwardNavigation ? Visibility.Visible : Visibility.Collapsed;
			DeveloperConsoleMenuItem.Visibility = WindowSettings.AllowDeveloperConsole ? Visibility.Visible : Visibility.Collapsed;
			FindMenuItem.Visibility = Visibility.Visible; // settings.AllowFind ? Visibility.Visible : Visibility.Collapsed;
			ForwardButton.IsEnabled = WindowSettings.AllowForwardNavigation;
			ForwardButton.Visibility = Visibility.Visible; // WindowSettings.AllowForwardNavigation ? Visibility.Visible : Visibility.Collapsed;
			HomeButton.IsEnabled = WindowSettings.ShowHomeButton;
			HomeButton.Visibility = Visibility.Visible; // WindowSettings.ShowHomeButton ? Visibility.Visible : Visibility.Collapsed;
			MenuButton.IsEnabled = settings.AllowPageZoom || WindowSettings.AllowDeveloperConsole;
			ReloadButton.IsEnabled = true; // WindowSettings.AllowReloading;
			ReloadButton.Visibility = Visibility.Visible; // WindowSettings.ShowReloadButton ? Visibility.Visible : Visibility.Collapsed;
			Toolbar.Visibility = Visibility.Visible; // WindowSettings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
			UrlTextBox.Visibility = Visibility.Visible; // WindowSettings.AllowAddressBar ? Visibility.Visible : Visibility.Hidden;
			ZoomMenuItem.Visibility = Visibility.Visible; // settings.AllowPageZoom ? Visibility.Visible : Visibility.Collapsed;
		}

		private void InitializeBounds()
		{
			if (isMainWindow && WindowSettings.FullScreenMode)
			{
				Top = 0;
				Left = 0;
				Height = SystemParameters.WorkArea.Height;
				Width = SystemParameters.WorkArea.Width;
				ResizeMode = ResizeMode.NoResize;
				WindowStyle = WindowStyle.None;
			}
			else if (WindowSettings.RelativeHeight == 100 && WindowSettings.RelativeWidth == 100)
			{
				WindowState = WindowState.Maximized;
			}
			else
			{
				if (WindowSettings.RelativeHeight > 0)
				{
					Height = SystemParameters.WorkArea.Height * WindowSettings.RelativeHeight.Value / 100;
					Top = (SystemParameters.WorkArea.Height / 2) - (Height / 2);
				}
				else if (WindowSettings.AbsoluteHeight > 0)
				{
					Height = this.TransformFromPhysical(0, WindowSettings.AbsoluteHeight.Value).Y;
					Top = (SystemParameters.WorkArea.Height / 2) - (Height / 2);
				}

				if (WindowSettings.RelativeWidth > 0)
				{
					Width = SystemParameters.WorkArea.Width * WindowSettings.RelativeWidth.Value / 100;
				}
				else if (WindowSettings.AbsoluteWidth > 0)
				{
					Width = this.TransformFromPhysical(WindowSettings.AbsoluteWidth.Value, 0).X;
				}

				if (Height > SystemParameters.WorkArea.Height)
				{
					Top = 0;
					Height = SystemParameters.WorkArea.Height;
				}

				if (Width > SystemParameters.WorkArea.Width)
				{
					Left = 0;
					Width = SystemParameters.WorkArea.Width;
				}

				switch (WindowSettings.Position)
				{
					case WindowPosition.Left:
						Left = 0;
						break;
					case WindowPosition.Center:
						Left = (SystemParameters.WorkArea.Width / 2) - (Width / 2);
						break;
					case WindowPosition.Right:
						Left = SystemParameters.WorkArea.Width - Width;
						break;
				}
			}
		}

		private void LoadIcons()
		{
			var backward = new XamlIconResource { Uri = new Uri("pack://application:,,,/SafeExamBrowser.UserInterface.Desktop;component/Images/NavigateBack.xaml") };
			var forward = new XamlIconResource { Uri = new Uri("pack://application:,,,/SafeExamBrowser.UserInterface.Desktop;component/Images/NavigateForward.xaml") };
			var home = new XamlIconResource { Uri = new Uri("pack://application:,,,/SafeExamBrowser.UserInterface.Desktop;component/Images/Home.xaml") };
			var menu = new XamlIconResource { Uri = new Uri("pack://application:,,,/SafeExamBrowser.UserInterface.Desktop;component/Images/Menu.xaml") };
			var reload = new XamlIconResource { Uri = new Uri("pack://application:,,,/SafeExamBrowser.UserInterface.Desktop;component/Images/Reload.xaml") };

			BackwardButton.Content = IconResourceLoader.Load(backward);
			ForwardButton.Content = IconResourceLoader.Load(forward);
			HomeButton.Content = IconResourceLoader.Load(home);
			MenuButton.Content = IconResourceLoader.Load(menu);
			ReloadButton.Content = IconResourceLoader.Load(reload);
		}

		private void LoadText()
		{
			DeveloperConsoleText.Text = text.Get(TextKey.BrowserWindow_DeveloperConsoleMenuItem);
			FindCaseSensitiveCheckBox.Content = text.Get(TextKey.BrowserWindow_FindCaseSensitive);
			FindMenuText.Text = text.Get(TextKey.BrowserWindow_FindMenuItem);
			ZoomText.Text = text.Get(TextKey.BrowserWindow_ZoomMenuItem);
		}

		public void FocusToolbar(bool forward)
		{
			this.Dispatcher.BeginInvoke((Action)(async () =>
			{
				this.Activate();
				await Task.Delay(50);

				// focus all elements in the toolbar, such that the last element that is enabled gets focus
				var buttons = new System.Windows.Controls.Control[] { ForwardButton, BackwardButton, ReloadButton, UrlTextBox, MenuButton, };
				for (var i = forward ? 0 : buttons.Length - 1; i >= 0 && i < buttons.Length; i += forward ? 1 : -1)
				{
					if (buttons[i].IsEnabled && buttons[i].Visibility == Visibility.Visible)
					{
						buttons[i].Focus();
						break;
					}
				}
			}));
		}

		public void FocusBrowser()
		{
			this.Dispatcher.BeginInvoke((Action)(async () =>
			{
				this.FocusToolbar(false);
				await Task.Delay(100);

				this.browserControlGetsFocusFromTaskbar = true;

				var focusedElement = FocusManager.GetFocusedElement(this) as UIElement;
				focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));

				await Task.Delay(150);
				this.browserControlGetsFocusFromTaskbar = false;
			}));
		}
	}
}
