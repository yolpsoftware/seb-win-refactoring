﻿/*
 * Copyright (c) 2017 ETH Zürich, Educational Development and Technology (LET)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System.Collections.Generic;
using SafeExamBrowser.Browser;
using SafeExamBrowser.Configuration;
using SafeExamBrowser.Contracts.Behaviour;
using SafeExamBrowser.Contracts.Configuration;
using SafeExamBrowser.Contracts.Configuration.Settings;
using SafeExamBrowser.Contracts.I18n;
using SafeExamBrowser.Contracts.Logging;
using SafeExamBrowser.Contracts.Monitoring;
using SafeExamBrowser.Contracts.UserInterface;
using SafeExamBrowser.Contracts.WindowsApi;
using SafeExamBrowser.Core.Behaviour;
using SafeExamBrowser.Core.Behaviour.Operations;
using SafeExamBrowser.Core.I18n;
using SafeExamBrowser.Core.Logging;
using SafeExamBrowser.Monitoring.Keyboard;
using SafeExamBrowser.Monitoring.Mouse;
using SafeExamBrowser.Monitoring.Processes;
using SafeExamBrowser.Monitoring.Windows;
using SafeExamBrowser.UserInterface;
using SafeExamBrowser.WindowsApi;

namespace SafeExamBrowser
{
	internal class CompositionRoot
	{
		private IApplicationController browserController;
		private IApplicationInfo browserInfo;
		private IKeyboardInterceptor keyboardInterceptor;
		private ILogger logger;
		private IMouseInterceptor mouseInterceptor;
		private INativeMethods nativeMethods;
		private IProcessMonitor processMonitor;
		private IRuntimeController runtimeController;
		private ISettings settings;
		private IText text;
		private ITextResource textResource;
		private IUserInterfaceFactory uiFactory;
		private IWindowMonitor windowMonitor;
		private IWorkingArea workingArea;

		public IShutdownController ShutdownController { get; private set; }
		public IStartupController StartupController { get; private set; }
		public Queue<IOperation> StartupOperations { get; private set; }
		public Taskbar Taskbar { get; private set; }

		public void BuildObjectGraph()
		{
			browserInfo = new BrowserApplicationInfo();
			logger = new Logger();
			nativeMethods = new NativeMethods();
			settings = new SettingsImpl();
			Taskbar = new Taskbar();
			textResource = new XmlTextResource();
			uiFactory = new UserInterfaceFactory();

			logger.Subscribe(new LogFileWriter(settings));

			text = new Text(textResource);
			browserController = new BrowserApplicationController(settings, text, uiFactory);
			keyboardInterceptor = new KeyboardInterceptor(settings.Keyboard, new ModuleLogger(logger, typeof(KeyboardInterceptor)));
			mouseInterceptor = new MouseInterceptor(new ModuleLogger(logger, typeof(MouseInterceptor)), settings.Mouse);
			processMonitor = new ProcessMonitor(new ModuleLogger(logger, typeof(ProcessMonitor)), nativeMethods);
			windowMonitor = new WindowMonitor(new ModuleLogger(logger, typeof(WindowMonitor)), nativeMethods);
			workingArea = new WorkingArea(new ModuleLogger(logger, typeof(WorkingArea)), nativeMethods);

			runtimeController = new RuntimeController(new ModuleLogger(logger, typeof(RuntimeController)), processMonitor, Taskbar, windowMonitor, workingArea);
			ShutdownController = new ShutdownController(logger, settings, text, uiFactory);
			StartupController = new StartupController(logger, settings, text, uiFactory);

			StartupOperations = new Queue<IOperation>();
			StartupOperations.Enqueue(new KeyboardInterceptorOperation(keyboardInterceptor, logger, nativeMethods));
			StartupOperations.Enqueue(new WindowMonitorOperation(logger, windowMonitor));
			StartupOperations.Enqueue(new ProcessMonitorOperation(logger, processMonitor));
			StartupOperations.Enqueue(new WorkingAreaOperation(logger, Taskbar, workingArea));
			StartupOperations.Enqueue(new TaskbarOperation(logger, settings, Taskbar, text, uiFactory));
			StartupOperations.Enqueue(new BrowserOperation(browserController, browserInfo, logger, Taskbar, uiFactory));
			StartupOperations.Enqueue(new RuntimeControllerOperation(runtimeController, logger));
			StartupOperations.Enqueue(new MouseInterceptorOperation(logger, mouseInterceptor, nativeMethods));
		}
	}
}
