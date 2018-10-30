﻿/*
 * Copyright (c) 2018 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.IO;
using SafeExamBrowser.Contracts.Browser;
using SafeExamBrowser.Contracts.Communication.Data;
using SafeExamBrowser.Contracts.Communication.Events;
using SafeExamBrowser.Contracts.Communication.Hosts;
using SafeExamBrowser.Contracts.Communication.Proxies;
using SafeExamBrowser.Contracts.Configuration;
using SafeExamBrowser.Contracts.Configuration.Settings;
using SafeExamBrowser.Contracts.Core;
using SafeExamBrowser.Contracts.Core.OperationModel;
using SafeExamBrowser.Contracts.Core.OperationModel.Events;
using SafeExamBrowser.Contracts.I18n;
using SafeExamBrowser.Contracts.Logging;
using SafeExamBrowser.Contracts.Monitoring;
using SafeExamBrowser.Contracts.UserInterface;
using SafeExamBrowser.Contracts.UserInterface.MessageBox;
using SafeExamBrowser.Contracts.UserInterface.Taskbar;
using SafeExamBrowser.Contracts.UserInterface.Windows;
using SafeExamBrowser.Contracts.WindowsApi;

namespace SafeExamBrowser.Client
{
	internal class ClientController : IClientController
	{
		private IDisplayMonitor displayMonitor;
		private IExplorerShell explorerShell;
		private ILogger logger;
		private IMessageBox messageBox;
		private IOperationSequence operations;
		private IProcessMonitor processMonitor;
		private IRuntimeProxy runtime;
		private Action shutdown;
		private ISplashScreen splashScreen;
		private ITaskbar taskbar;
		private IText text;
		private IUserInterfaceFactory uiFactory;
		private IWindowMonitor windowMonitor;
		private AppConfig appConfig;

		public IBrowserApplicationController Browser { private get; set; }
		public IClientHost ClientHost { private get; set; }
		public Guid SessionId { private get; set; }
		public Settings Settings { private get; set; }

		public AppConfig AppConfig
		{
			set
			{
				appConfig = value;

				if (splashScreen != null)
				{
					splashScreen.AppConfig = value;
				}
			}
		}

		public ClientController(
			IDisplayMonitor displayMonitor,
			IExplorerShell explorerShell,
			ILogger logger,
			IMessageBox messageBox,
			IOperationSequence operations,
			IProcessMonitor processMonitor,
			IRuntimeProxy runtime,
			Action shutdown,
			ITaskbar taskbar,
			IText text,
			IUserInterfaceFactory uiFactory,
			IWindowMonitor windowMonitor)
		{
			this.displayMonitor = displayMonitor;
			this.explorerShell = explorerShell;
			this.logger = logger;
			this.messageBox = messageBox;
			this.operations = operations;
			this.processMonitor = processMonitor;
			this.runtime = runtime;
			this.shutdown = shutdown;
			this.taskbar = taskbar;
			this.text = text;
			this.uiFactory = uiFactory;
			this.windowMonitor = windowMonitor;
		}

		public bool TryStart()
		{
			logger.Info("Initiating startup procedure...");

			splashScreen = uiFactory.CreateSplashScreen();
			operations.ProgressChanged += Operations_ProgressChanged;
			operations.StatusChanged += Operations_StatusChanged;

			var success = operations.TryPerform() == OperationResult.Success;

			if (success)
			{
				RegisterEvents();

				var communication = runtime.InformClientReady();

				if (communication.Success)
				{
					splashScreen.Close();

					logger.Info("Application successfully initialized.");
					logger.Log(string.Empty);
				}
				else
				{
					success = false;
					logger.Error("Failed to inform runtime that client is ready!");
				}
			}
			else
			{
				logger.Info("Application startup aborted!");
				logger.Log(string.Empty);
			}

			return success;
		}

		public void Terminate()
		{
			logger.Log(string.Empty);
			logger.Info("Initiating shutdown procedure...");

			splashScreen = uiFactory.CreateSplashScreen(appConfig);
			splashScreen.Show();

			DeregisterEvents();

			var success = operations.TryRevert() == OperationResult.Success;

			if (success)
			{
				logger.Info("Application successfully finalized.");
				logger.Log(string.Empty);
			}
			else
			{
				logger.Info("Shutdown procedure failed!");
				logger.Log(string.Empty);
			}

			splashScreen.Close();
		}

		private void RegisterEvents()
		{
			Browser.ConfigurationDownloadRequested += Browser_ConfigurationDownloadRequested;
			ClientHost.PasswordRequested += ClientHost_PasswordRequested;
			ClientHost.ReconfigurationDenied += ClientHost_ReconfigurationDenied;
			ClientHost.Shutdown += ClientHost_Shutdown;
			displayMonitor.DisplayChanged += DisplayMonitor_DisplaySettingsChanged;
			processMonitor.ExplorerStarted += ProcessMonitor_ExplorerStarted;
			runtime.ConnectionLost += Runtime_ConnectionLost;
			taskbar.QuitButtonClicked += Taskbar_QuitButtonClicked;
			windowMonitor.WindowChanged += WindowMonitor_WindowChanged;
		}

		private void DeregisterEvents()
		{
			Browser.ConfigurationDownloadRequested -= Browser_ConfigurationDownloadRequested;
			ClientHost.PasswordRequested -= ClientHost_PasswordRequested;
			ClientHost.ReconfigurationDenied -= ClientHost_ReconfigurationDenied;
			ClientHost.Shutdown -= ClientHost_Shutdown;
			displayMonitor.DisplayChanged -= DisplayMonitor_DisplaySettingsChanged;
			processMonitor.ExplorerStarted -= ProcessMonitor_ExplorerStarted;
			runtime.ConnectionLost -= Runtime_ConnectionLost;
			taskbar.QuitButtonClicked -= Taskbar_QuitButtonClicked;
			windowMonitor.WindowChanged -= WindowMonitor_WindowChanged;
		}

		private void DisplayMonitor_DisplaySettingsChanged()
		{
			logger.Info("Reinitializing working area...");
			displayMonitor.InitializePrimaryDisplay(taskbar.GetAbsoluteHeight());
			logger.Info("Reinitializing taskbar bounds...");
			taskbar.InitializeBounds();
			logger.Info("Desktop successfully restored.");
		}

		private void ProcessMonitor_ExplorerStarted()
		{
			logger.Info("Trying to terminate Windows explorer...");
			explorerShell.Terminate();
			logger.Info("Reinitializing working area...");
			displayMonitor.InitializePrimaryDisplay(taskbar.GetAbsoluteHeight());
			logger.Info("Reinitializing taskbar bounds...");
			taskbar.InitializeBounds();
			logger.Info("Desktop successfully restored.");
		}

		private void Browser_ConfigurationDownloadRequested(string fileName, DownloadEventArgs args)
		{
			if (Settings.ConfigurationMode == ConfigurationMode.ConfigureClient)
			{
				logger.Debug($"Received download request for configuration file '{fileName}'. Asking user to confirm the reconfiguration...");

				var message = TextKey.MessageBox_ReconfigurationQuestion;
				var title = TextKey.MessageBox_ReconfigurationQuestionTitle;
				var result = messageBox.Show(message, title, MessageBoxAction.YesNo, MessageBoxIcon.Question, args.BrowserWindow);
				var reconfigure = result == MessageBoxResult.Yes;

				logger.Info($"The user chose to {(reconfigure ? "start" : "abort")} the reconfiguration.");

				if (reconfigure)
				{
					args.AllowDownload = true;
					args.Callback = Browser_ConfigurationDownloadFinished;
					args.DownloadPath = Path.Combine(appConfig.DownloadDirectory, fileName);
				}
			}
			else
			{
				logger.Info($"Denied download request for configuration file '{fileName}' due to '{Settings.ConfigurationMode}' mode.");
				messageBox.Show(TextKey.MessageBox_ReconfigurationDenied, TextKey.MessageBox_ReconfigurationDeniedTitle, parent: args.BrowserWindow);
			}
		}

		private void Browser_ConfigurationDownloadFinished(bool success, string filePath = null)
		{
			if (success)
			{
				var communication = runtime.RequestReconfiguration(filePath);

				if (communication.Success)
				{
					logger.Info($"Sent reconfiguration request for '{filePath}' to the runtime.");
				}
				else
				{
					logger.Error($"Failed to communicate reconfiguration request for '{filePath}'!");
					messageBox.Show(TextKey.MessageBox_ReconfigurationError, TextKey.MessageBox_ReconfigurationErrorTitle, icon: MessageBoxIcon.Error);
				}
			}
			else
			{
				logger.Error($"Failed to download configuration file '{filePath}'!");
				messageBox.Show(TextKey.MessageBox_ConfigurationDownloadError, TextKey.MessageBox_ConfigurationDownloadErrorTitle, icon: MessageBoxIcon.Error);
			}
		}

		private void ClientHost_PasswordRequested(PasswordRequestEventArgs args)
		{
			var isAdmin = args.Purpose == PasswordRequestPurpose.Administrator;
			var message = isAdmin ? TextKey.PasswordDialog_AdminPasswordRequired : TextKey.PasswordDialog_SettingsPasswordRequired;
			var title = isAdmin ? TextKey.PasswordDialog_AdminPasswordRequiredTitle : TextKey.PasswordDialog_SettingsPasswordRequiredTitle;
			var dialog = uiFactory.CreatePasswordDialog(text.Get(message), text.Get(title));

			logger.Info($"Received input request with id '{args.RequestId}' for the {args.Purpose.ToString().ToLower()} password.");

			var result = dialog.Show();

			runtime.SubmitPassword(args.RequestId, result.Success, result.Password);
			logger.Info($"Password request with id '{args.RequestId}' was {(result.Success ? "successful" : "aborted by the user")}.");
		}

		private void ClientHost_ReconfigurationDenied(ReconfigurationEventArgs args)
		{
			logger.Info($"The reconfiguration request for '{args.ConfigurationPath}' was denied by the runtime!");
			messageBox.Show(TextKey.MessageBox_ReconfigurationDenied, TextKey.MessageBox_ReconfigurationDeniedTitle);
		}

		private void ClientHost_Shutdown()
		{
			taskbar.Close();
			shutdown.Invoke();
		}

		private void Operations_ProgressChanged(ProgressChangedEventArgs args)
		{
			if (args.CurrentValue.HasValue)
			{
				splashScreen?.SetValue(args.CurrentValue.Value);
			}

			if (args.IsIndeterminate == true)
			{
				splashScreen?.SetIndeterminate();
			}

			if (args.MaxValue.HasValue)
			{
				splashScreen?.SetMaxValue(args.MaxValue.Value);
			}

			if (args.Progress == true)
			{
				splashScreen?.Progress();
			}

			if (args.Regress == true)
			{
				splashScreen?.Regress();
			}
		}

		private void Operations_StatusChanged(TextKey status)
		{
			splashScreen?.UpdateStatus(status, true);
		}

		private void Runtime_ConnectionLost()
		{
			logger.Error("Lost connection to the runtime!");
			messageBox.Show(TextKey.MessageBox_ApplicationError, TextKey.MessageBox_ApplicationErrorTitle, icon: MessageBoxIcon.Error);

			taskbar.Close();
			shutdown.Invoke();
		}

		private void Taskbar_QuitButtonClicked(System.ComponentModel.CancelEventArgs args)
		{
			var result = messageBox.Show(TextKey.MessageBox_Quit, TextKey.MessageBox_QuitTitle, MessageBoxAction.YesNo, MessageBoxIcon.Question);

			if (result == MessageBoxResult.Yes)
			{
				var communication = runtime.RequestShutdown();

				if (!communication.Success)
				{
					logger.Error("Failed to communicate shutdown request to the runtime!");
					messageBox.Show(TextKey.MessageBox_QuitError, TextKey.MessageBox_QuitErrorTitle, icon: MessageBoxIcon.Error);
				}
			}
			else
			{
				args.Cancel = true;
			}
		}

		private void WindowMonitor_WindowChanged(IntPtr window)
		{
			var allowed = processMonitor.BelongsToAllowedProcess(window);

			if (!allowed)
			{
				var success = windowMonitor.Hide(window);

				if (!success)
				{
					windowMonitor.Close(window);
				}
			}
		}
	}
}
