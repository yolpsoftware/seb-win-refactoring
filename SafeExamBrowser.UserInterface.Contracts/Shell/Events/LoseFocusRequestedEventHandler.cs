/*
 * Copyright (c) 2021 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System.ComponentModel;

namespace SafeExamBrowser.UserInterface.Contracts.Shell.Events
{
	/// <summary>
	/// Event handler used to indicate that the current control/window wants to lose its focus.
	/// </summary>
	public delegate void LoseFocusRequestedEventHandler(bool forward);
}
