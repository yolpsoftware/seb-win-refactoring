﻿/*
 * Copyright (c) 2022 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System.Linq;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.SystemComponents.Contracts;

namespace SafeExamBrowser.SystemComponents
{
	public class VirtualMachineDetector : IVirtualMachineDetector
	{
		/// <summary>
		/// Virtualbox: VBOX, 80EE / RedHat: QUEMU, 1AF4, 1B36
		/// </summary>
		private static readonly string[] PCI_VENDOR_BLACKLIST =
		{
			"vbox", "vid_80ee", "qemu", "ven_1af4", "ven_1b36", "subsys_11001af4", "PROD_VMWARE", "VEN_VMWARE", "VMWARE_IDE"
		};
		private static readonly string QEMU_MAC_PREFIX = "525400";
		private static readonly string VIRTUALBOX_MAC_PREFIX = "080027";

		private readonly ILogger logger;
		private readonly ISystemInfo systemInfo;

		public VirtualMachineDetector(ILogger logger, ISystemInfo systemInfo)
		{
			this.logger = logger;
			this.systemInfo = systemInfo;
		}

		public bool IsVirtualMachine()
		{
			var biosInfo = systemInfo.BiosInfo.ToLower();
			var isVirtualMachine = false;
			var macAddress = systemInfo.MacAddress;
			var manufacturer = systemInfo.Manufacturer.ToLower();
			var model = systemInfo.Model.ToLower();
			var plugAndPlayDeviceIds = systemInfo.PlugAndPlayDeviceIds;

			isVirtualMachine |= biosInfo.Contains("vmware");
			isVirtualMachine |= biosInfo.Contains("virtualbox");
			isVirtualMachine |= manufacturer.Contains("microsoft corporation") && !model.Contains("surface");
			isVirtualMachine |= manufacturer.Contains("vmware");
			isVirtualMachine |= manufacturer.Contains("parallels software");
			isVirtualMachine |= model.Contains("virtualbox");
			isVirtualMachine |= manufacturer.Contains("qemu");

			if (macAddress != null && macAddress.Count() > 2)
			{
				isVirtualMachine |= macAddress.StartsWith(QEMU_MAC_PREFIX) || macAddress.StartsWith(VIRTUALBOX_MAC_PREFIX);
			}

			foreach (var device in plugAndPlayDeviceIds)
			{
				isVirtualMachine |= PCI_VENDOR_BLACKLIST.Any(v => device.ToLower().Contains(v.ToLower()));
			}

			logger.Debug($"Computer '{systemInfo.Name}' appears {(isVirtualMachine ? "" : "not ")}to be a virtual machine.");

			return isVirtualMachine;
		}
	}
}
