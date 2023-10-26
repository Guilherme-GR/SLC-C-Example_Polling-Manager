﻿namespace Skyline.PollingManager.Client
{
    using System.Collections.Generic;
    using System.Linq;

    using Skyline.PollingManager.Enums;
    using Skyline.PollingManager.Pollable;
    using Skyline.PollingManager.Providers;
    using Skyline.PollingManager.Structs;

    public static class PollingManagerConfiguration
	{
		private static readonly Dictionary<string, PollableBase> _rows = new Dictionary<string, PollableBase>()
		{
			// Parent of CEO, CFO, CTO
			// Child of -
			{ "Owner", new PollableA(SLProtocolProvider.Protocol, "Owner - A") },

			// Parent of CFO, CTO, Expert Hub Lead
			// Child of Owner
			{ "CEO", new PollableA(SLProtocolProvider.Protocol, "CEO - A") },

			// Parent of -
			// Child of Owner, CEO
			{ "CFO", new PollableB(SLProtocolProvider.Protocol, "CFO - B", 15) },

			// Parent of Expert Hub Lead
			// Child of Owner, CEO
			{ "CTO", new PollableB(SLProtocolProvider.Protocol, "CTO - B", 20) },

			// Parent of Principal 1, Principal 2, Senior 1
			// Child of CEO, CTO
			{ "Expert Hub Lead", new PollableB(SLProtocolProvider.Protocol, "Expert Hub Lead - B") },

			// Parent of Senior 2, Senior 3
			// Child of CTO, Expert Hub Lead
			{ "Principal 1", new PollableB(SLProtocolProvider.Protocol, "Principal 1 - B") },

			// Parent of Senior 1
			// Child of CTO, Expert Hub Lead
			{ "Principal 2", new PollableC(SLProtocolProvider.Protocol, "Principal 2 - C", 30, 60, PeriodType.Default) },

			// Parent of -
			// Child of Expert Hub Lead, Principal 2
			{ "Senior 1", new PollableA(SLProtocolProvider.Protocol, "Senior 1 - A") },

			// Parent of -
			// Child of Principal 1
			{ "Senior 2", new PollableB(SLProtocolProvider.Protocol, "Senior 2 - B") },

			// Parent of -
			// Child of Principal 1
			{ "Senior 3", new PollableC(SLProtocolProvider.Protocol, "Senior 3 - C") },
		};

		private static readonly List<Dependency> _dependencies = new List<Dependency>()
		{
		};

		static PollingManagerConfiguration()
		{
			SetRelations();
		}

		public static List<PollableBase> Rows => _rows.Select(row => row.Value).ToList();

		private static void SetRelations()
		{
			_rows["Owner"].AddChildren(_rows["CEO"], _rows["CFO"], _rows["CTO"]);

			_rows["CEO"].AddChildren(_rows["CFO"], _rows["CTO"], _rows["Expert Hub Lead"]);

			_rows["CTO"].AddChildren(_rows["Expert Hub Lead"]);

			_rows["Principal 1"].AddParents(_rows["CTO"], _rows["Expert Hub Lead"]);
			_rows["Principal 1"].AddChildren(_rows["Senior 2"], _rows["Senior 3"]);

			_rows["Principal 2"].AddParents(_rows["CTO"], _rows["Expert Hub Lead"]);
			_rows["Principal 2"].AddChildren(_rows["Senior 1"]);

			_rows["Senior 1"].AddParents(_rows["Expert Hub Lead"]);
		}

		private static void SetDependencies()
		{
		}
	}
}
