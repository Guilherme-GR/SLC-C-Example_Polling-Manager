﻿namespace Skyline.PollingManager
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Skyline.DataMiner.Scripting;

    using Skyline.PollingManager.Enums;
    using Skyline.PollingManager.Interfaces;

    /// <summary>
    /// <see cref="PollingManager"/> container class used to provide singleton on the element level.
    /// </summary>
    public static class PollingManagerContainer
    {
        private static Dictionary<string, PollingManager> _managers = new Dictionary<string, PollingManager>();

		/// <summary>
		/// Creates instance of <see cref="PollingManager"/> and adds it to <see cref="PollingManagerContainer"/>.
		/// </summary>
		/// <param name="protocol">Link with SLProtocol process.</param>
		/// <param name="table">Polling manager table instance.</param>
		/// <param name="rows">Rows to add to the <paramref name="table"/>.</param>
		/// <param name="pollableFactory">Factory for concrete implementation of <see cref="PollableBase"/>.</param>
		/// <returns>
        /// Newly created instance of <see cref="PollingManager"/>, if it doesn't exist, or existing instance of <see cref="PollingManager"/> with updated <see cref="PollableBase.Protocol"/>.
        /// </returns>
        public static PollingManager AddManager(SLProtocol protocol, PollingmanagerQActionTable table, List<PollableBase> rows, IPollableBaseFactory pollableFactory)
        {
            string key = GetKey(protocol);

            if (!_managers.ContainsKey(key))
            {
                var manager = new PollingManager(protocol, table, rows, pollableFactory);

                _managers.Add(key, manager);
            }

            _managers[key].Protocol = protocol;

            return _managers[key];
        }

		/// <summary>
		/// Gets <see cref="PollingManager"/> instance for the element.
		/// </summary>
		/// <param name="protocol">Link with SLProtocol process.</param>
		/// <returns><see cref="PollingManager"/> instance with updated <see cref="PollableBase.Protocol"/>.</returns>
		/// <exception cref="InvalidOperationException">Throws if <see cref="PollingManager"/> for this element is not initialized.</exception>
        public static PollingManager GetManager(SLProtocol protocol)
        {
            string key = GetKey(protocol);

            if (!_managers.ContainsKey(key))
                throw new InvalidOperationException("Polling manager for this element is not initialized, please call AddManager first!");

            _managers[key].Protocol = protocol;

            return _managers[key];
        }

		/// <summary>
		/// Creates unique key based on DataMinerID and ElementID.
		/// </summary>
		/// <param name="protocol">Link with SLProtocol process.</param>
		/// <returns>Key in format DataMinerID/ElementID</returns>
        private static string GetKey(SLProtocol protocol)
        {
            return string.Join("/", protocol.DataMinerID, protocol.ElementID);
        }
    }

    /// <summary>
    /// Handler for <see cref="PollingmanagerQActionTable"/>.
    /// </summary>
    public class PollingManager
    {
        private readonly PollingmanagerQActionTable _table;
        private readonly Dictionary<string, PollableBase> _rows = new Dictionary<string, PollableBase>();
        private readonly IPollableBaseFactory _pollableFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="PollingManager"/> class.
		/// </summary>
		/// <param name="protocol">Link with SLProtocol process.</param>
		/// <param name="table">Polling manager table instance.</param>
		/// <param name="rows">Rows to add to the <paramref name="table"/>.</param>
		/// <param name="pollableFactory">Factory for concrete implementation of <see cref="PollableBase"/>.</param>
		/// <exception cref="ArgumentException">Throws if <paramref name="rows"/> contains duplicate names.</exception>
		/// <exception cref="ArgumentException">Throws if <paramref name="rows"/> contains null values.</exception>
        public PollingManager(SLProtocol protocol, PollingmanagerQActionTable table, List<PollableBase> rows, IPollableBaseFactory pollableFactory)
        {
            Protocol = protocol;
            _table = table;
            _pollableFactory = pollableFactory;

            HashSet<string> names = new HashSet<string>();

            for (int i = 0; i < rows.Count; i++)
            {
                if (!names.Add(rows[i].Name))
                    throw new ArgumentException($"Duplicate name: {rows[i].Name}");

                _rows.Add((i + 1).ToString(), rows[i] ?? throw new ArgumentException("Rows parameter can't contain null values!"));
            }

            FillTable(_rows);
        }

        public SLProtocol Protocol { get; set; }

        /// <summary>
        /// Checks <see cref="PollingmanagerQActionTable"/> for rows that are ready to be polled and polls them.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Throws if <see cref="PollableBase.PeriodType"/> is not <see cref="PeriodType.Default"/> or <see cref="PeriodType.Custom"/>.
        /// </exception>
        public void CheckForUpdate()
        {
            bool requiresUpdate = false;

            foreach (KeyValuePair<string, PollableBase> row in _rows)
            {
				PollableBase currentRow = row.Value;

				if (currentRow.State == State.Disabled)
                    continue;

				bool readyToPoll;
				bool pollSucceeded;

				switch (currentRow.PeriodType)
                {
                    case PeriodType.Default:
                        readyToPoll = CheckLastPollTime(currentRow.DefaultPeriod, currentRow.LastPoll);
                        break;

                    case PeriodType.Custom:
                        readyToPoll = CheckLastPollTime(currentRow.Period, currentRow.LastPoll);
                        break;

                    default:
                        throw new InvalidOperationException($"Unhandled PeriodType: {currentRow.PeriodType}");
                }

				if (readyToPoll && currentRow.CheckDependencies())
                {
                    pollSucceeded = currentRow.Poll();
                    currentRow.LastPoll = DateTime.Now;
                }
                else
                {
                    continue;
                }

				if (pollSucceeded)
					currentRow.Status = Status.Succeeded;
                else
					currentRow.Status = Status.Failed;

				requiresUpdate = true;
            }

            if (requiresUpdate)
                FillTableNoDelete(_rows);
        }

        /// <summary>
        /// Handles sets on the <see cref="PollingmanagerQActionTable"/>.
        /// </summary>
        /// <param name="id">Row key.</param>
        /// <param name="column">Column on which set was performed.</param>
        public void UpdateRow(string id, Column column)
        {
            PollableBase tableRow = CreateIPollable(_table.GetRow(id));

            switch (column)
            {
                case Column.Period:
                    tableRow.PeriodType = PeriodType.Custom;
                    break;

                case Column.PeriodType:
                    if (tableRow.PeriodType == PeriodType.Custom)
                        tableRow.Period = _rows[id].Period;

                    break;

                case Column.Poll:
                    PollRow(tableRow);
                    break;

                case Column.State:
                    HandleStateUpdate(tableRow);
                    break;

                default:
                    break;
            }

            UpdateInternalRow(id, tableRow);
            FillTableNoDelete(_rows);
        }

		/// <summary>
		/// Handles context menu actions for the <see cref="PollingmanagerQActionTable"/>.
		/// </summary>
		/// <param name="contextMenu">Object that contains information related to the context menu.</param>
        /// <exception cref="ArgumentException">Throws if <paramref name="contextMenu"/> is not of type string[].</exception>
        /// <exception cref="ArgumentException">
        /// Throws if second element of converted <paramref name="contextMenu"/> can't be parsed as int.
        /// </exception>
        public void HandleContextMenu(object contextMenu)
        {
            var input = contextMenu as string[];

            if (input == null)
                throw new ArgumentException("Parameter contextMenu can't be converted to string[]!");

            if (!int.TryParse(input[1], out int selectedOption))
                throw new ArgumentException("Unable to parse selected option from parameter contextMenu!");

            switch ((ContextMenuOption)selectedOption)
            {
                case ContextMenuOption.PollAll:
                    PollAll();
                    break;

                case ContextMenuOption.DisableAll:
                    DisableAll();
                    break;

                case ContextMenuOption.EnableAll:
                    EnableAll();
                    break;

                case ContextMenuOption.DisableSelected:
                    DisableSelected(input.Skip(2).ToArray());
                    break;

                case ContextMenuOption.EnableSelected:
                    EnableSelected(input.Skip(2).ToArray());
                    break;

                default:
                    break;
            }

            FillTableNoDelete(_rows);
        }

        /// <summary>
        /// Polls all rows by calling <see cref="PollRow"/> for every row.
        /// </summary>
        private void PollAll()
        {
            foreach (KeyValuePair<string, PollableBase> row in _rows)
            {
                PollRow(row.Value);
            }
        }

        /// <summary>
        /// Disables all rows.
        /// </summary>
        private void DisableAll()
        {
            foreach (KeyValuePair<string, PollableBase> row in _rows)
            {
                row.Value.State = State.Disabled;
            }
        }

        /// <summary>
        /// Enables all rows.
        /// </summary>
        private void EnableAll()
        {
            foreach (KeyValuePair<string, PollableBase> row in _rows)
            {
                row.Value.State = State.Enabled;
            }
        }

        // TODO
        private void DisableSelected(string[] rows)
        {
            foreach (string row in rows)
            {
                _rows[row].State = State.Disabled;
            }
        }

        // TODO
        private void EnableSelected(string[] rows)
        {
            foreach (string row in rows)
            {
                _rows[row].State = State.Enabled;
            }
        }

        /// <summary>
        /// Handles state update logic.
        /// </summary>
        /// <param name="row">Row for which to update state.</param>
        private void HandleStateUpdate(IPollable row)
        {
            switch (row.State)
            {
                case State.Disabled:
                    if (row.Children.Any(child => child.State == State.Enabled))
                    {
                        ShowChildren(row);
                        row.State = State.Enabled;
                        return;
                    }

                    row.Status = Status.Disabled;
                    UpdateRelatedStates(row.Children, State.Disabled);
                    return;

                case State.Enabled:
                    if (row.Parents.Any(parent => parent.State == State.Disabled))
                    {
                        ShowParents(row);
                        row.State = State.Disabled;
                    }

                    return;

                case State.ForceDisabled:
                    row.State = State.Disabled;
                    row.Status = Status.Disabled;
                    UpdateRelatedStates(row.Children, State.ForceDisabled);
                    return;

                case State.ForceEnabled:
                    row.State = State.Enabled;
                    UpdateRelatedStates(row.Parents, State.ForceEnabled);
                    return;
            }
        }

        /// <summary>
        /// Shows information message with child rows of the row passed as parameter.
        /// </summary>
        /// <param name="row">Row for which to show children.</param>
        private void ShowChildren(IPollable row)
        {
            string children = string.Join("\n", row.Children.Select(child => child.Name));

            string message = $"Unable to disable [{row.Name}] because the following rows are dependand on it:\n{children}\nPlease disable them first or use [Force Disable].";

            Protocol.ShowInformationMessage(message);
        }

		/// <summary>
		/// Shows information message with parent rows of the row passed as parameter.
		/// </summary>
		/// <param name="row">Row for which to show parents.</param>
        private void ShowParents(IPollable row)
        {
            string parents = string.Join("\n", row.Parents.Select(parent => parent.Name));

            string message = $"Unable to enable [{row.Name}] because it depends on the following rows:\n{parents}\nPlease enable them first or use [Force Enable].";

            Protocol.ShowInformationMessage(message);
        }

        /// <summary>
        /// Updates states of the related rows.
        /// </summary>
        /// <param name="collection">List of rows to update.</param>
        /// <param name="state">State to update rows to.</param>
        private void UpdateRelatedStates(List<IPollable> collection, State state)
        {
            foreach (IPollable item in collection)
            {
                item.Status = Status.Disabled;
                item.State = state;
                HandleStateUpdate(item);
            }
        }

        /// <summary>
        /// Polls a row.
        /// </summary>
        /// <param name="row">Row to poll.</param>
        private void PollRow(PollableBase row)
        {
            if (row.State == State.Disabled)
                return;

            if (!row.CheckDependencies())
                return;

            bool pollSucceeded = row.Poll();
            row.LastPoll = DateTime.Now;

            if (pollSucceeded)
                row.Status = Status.Succeeded;
            else
                row.Status = Status.Failed;
        }

        /// <summary>
        /// Updates internal representation of the row.
        /// </summary>
        /// <param name="id">Row key.</param>
        /// <param name="newValue">Values to update the row to.</param>
        private void UpdateInternalRow(string id, PollableBase newValue)
        {
            _rows[id].Name = newValue.Name;
            _rows[id].Period = newValue.Period;
            _rows[id].DefaultPeriod = newValue.DefaultPeriod;
            _rows[id].PeriodType = newValue.PeriodType;
            _rows[id].LastPoll = newValue.LastPoll;
            _rows[id].Status = newValue.Status;
            _rows[id].State = newValue.State;
            _rows[id].Parents = newValue.Parents;
            _rows[id].Children = newValue.Children;
        }

        /// <summary>
        /// Checks whether poll period has elapsed.
        /// </summary>
        /// <param name="period">Poll period.</param>
        /// <param name="lastPoll">Last poll timestamp.</param>
        /// <returns>True if poll period has elapsed, false otherwise.</returns>
        private bool CheckLastPollTime(double period, DateTime lastPoll)
        {
            if ((DateTime.Now - lastPoll).TotalSeconds > period)
                return true;

            return false;
        }

		/// <summary>
		/// Sets the content of the table to the provided content.
		/// </summary>
		/// <param name="rows">Rows to fill the table with.</param>
        private void FillTable(Dictionary<string, PollableBase> rows)
        {
            PollingmanagerQActionRow[] tableRows = CreateTableRows(rows);

            _table.FillArray(tableRows);
        }

		/// <summary>
		/// Adds the provided row to the table.
		/// </summary>
		/// <param name="key">Row key.</param>
		/// <param name="value">Row to add.</param>
        private void FillTableNoDelete(string key, PollableBase value)
        {
            FillTableNoDelete(new Dictionary<string, PollableBase> { { key, value } });
        }

        /// <summary>
        /// Add the provided rows to the table.
        /// </summary>
        /// <param name="rows">Rows to add to the table.</param>
        private void FillTableNoDelete(Dictionary<string, PollableBase> rows)
        {
            PollingmanagerQActionRow[] tableRows = CreateTableRows(rows);

            _table.FillArrayNoDelete(tableRows);
        }

        /// <summary>
        /// Creates the <see cref="PollingmanagerQActionRow"/>.
        /// </summary>
        /// <param name="key">Row key.</param>
        /// <param name="value">Row to create.</param>
        /// <returns>Instance of <see cref="PollingmanagerQActionRow"/>.</returns>
        private PollingmanagerQActionRow CreateTableRow(string key, PollableBase value)
        {
            return new PollingmanagerQActionRow
            {
                Pollingmanagerindex_1001 = key,
                Pollingmanagername_1002 = value.Name,
                Pollingmanagerperiod_1003 = value.PeriodType == PeriodType.Custom ? value.Period : value.DefaultPeriod,
                Pollingmanagerdefaultperiod_1004 = value.DefaultPeriod,
                Pollingmanagerperiodtype_1005 = value.PeriodType,
                Pollingmanagerlastpoll_1006 = value.Status == Status.NotPolled ? (double)Status.NotPolled : value.LastPoll.ToOADate(),
                Pollingmanagerstatus_1007 = value.State == State.Disabled ? -1 : Convert.ToInt32(value.Status),
                Pollingmanagerstate_1009 = value.State,
            };
        }

        /// <summary>
        /// Creates the array of the <see cref="PollingmanagerQActionRow"/>.
        /// </summary>
        /// <param name="rows">Rows to create.</param>
        /// <returns>Array of the <see cref="PollingmanagerQActionRow"/>.</returns>
        private PollingmanagerQActionRow[] CreateTableRows(Dictionary<string, PollableBase> rows)
        {
            List<PollingmanagerQActionRow> tableRows = new List<PollingmanagerQActionRow>();

            foreach (KeyValuePair<string, PollableBase> row in rows)
            {
                tableRows.Add(CreateTableRow(row.Key, row.Value));
            }

            return tableRows.ToArray();
        }

		/// <summary>
		/// Creates an object that implements <see cref="PollableBase"/> by using <see cref="_pollableFactory"/> while preserving existing relations.
		/// </summary>
		/// <param name="tableRow">Table row returned by <see cref="QActionTable.GetRow(string)"/></param>
		/// <returns>Instance of the object that implements <see cref="PollableBase"/>.</returns>
		/// <exception cref="ArgumentException">Throws if first element in <paramref name="tableRow"/> is null or empty.</exception>
		/// <exception cref="InvalidOperationException">Throws if row key doesn't exist in the table.</exception>
        private PollableBase CreateIPollable(object[] tableRow)
        {
            string id = Convert.ToString(tableRow[0]);

            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Row key can't be null or empty!");

            if (!_rows.ContainsKey(id))
                throw new InvalidOperationException("Row key doesn't exist in the table!");

            PollableBase row = _pollableFactory.CreatePollableBase(Protocol, tableRow);

            row.Parents = _rows[id].Parents;
            row.Children = _rows[id].Children;

            return row;
        }
    }
}
