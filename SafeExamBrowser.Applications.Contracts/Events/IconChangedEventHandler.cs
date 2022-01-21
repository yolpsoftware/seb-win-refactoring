﻿/*
 * Copyright (c) 2022 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using SafeExamBrowser.Core.Contracts.Resources.Icons;

namespace SafeExamBrowser.Applications.Contracts.Events
{
	/// <summary>
	/// Event handler used to indicate that an icon has changed.
	/// </summary>
	public delegate void IconChangedEventHandler(IconResource icon);
}
