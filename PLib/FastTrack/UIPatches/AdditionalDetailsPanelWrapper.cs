﻿/*
 * Copyright 2024 Peter Han
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 * and associated documentation files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
 * BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using System.Text;
using UnityEngine;

using ELEMENTAL = STRINGS.UI.ELEMENTAL;

namespace PeterHan.FastTrack.UIPatches {
	/// <summary>
	/// Stores state information about the additional details panel to avoid recalculating so
	/// much every frame.
	/// </summary>
	public sealed partial class AdditionalDetailsPanelWrapper : System.IDisposable {
		/// <summary>
		/// Avoid reallocating a new StringBuilder every frame.
		/// </summary>
		private static readonly StringBuilder CACHED_BUILDER = new StringBuilder(64);

		/// <summary>
		/// The number of cycles to display uptime.
		/// </summary>
		private const int NUM_CYCLES = Tracker.defaultCyclesTracked;

		/// <summary>
		/// The singleton instance of this class.
		/// </summary>
		internal static AdditionalDetailsPanelWrapper Instance { get; private set; }

		/// <summary>
		/// Called at shutdown to avoid leaking references.
		/// </summary>
		internal static void Cleanup() {
			var inst = Instance;
			CACHED_BUILDER.Clear();
			if (inst != null)
				inst.Dispose();
			Instance = null;
		}

		/// <summary>
		/// Gets the overheat temperature modifier of an element.
		/// </summary>
		/// <param name="element">The selected element.</param>
		/// <returns>The amount that the element changes the overheat temperature, if any.</returns>
		private static string GetOverheatModifier(Element element) {
			string overheatID = Db.Get().BuildingAttributes.OverheatTemperature.Id,
				overheatStr = null;
			// Modify the overheat temperature accordingly
			var modifiers = element.attributeModifiers;
			int n = modifiers.Count;
			for (int i = 0; i < n; i++) {
				var modifier = modifiers[i];
				if (modifier.AttributeId == overheatID) {
					overheatStr = modifier.GetFormattedString();
					break;
				}
			}
			return overheatStr;
		}

		/// <summary>
		/// Gets the specific heat text for the details screen.
		/// </summary>
		/// <param name="element">The selected element.</param>
		/// <param name="tempUnits">The temperature units to use for formatting.</param>
		/// <param name="shcInfo">The specific heat information.</param>
		private static void GetSHCText(Element element, string tempUnits,
				out InfoLine shcInfo) {
			float shc = GameUtil.GetDisplaySHC(element.specificHeatCapacity);
			var text = CACHED_BUILDER;
			// Pass 1: float to string using Ryu
			text.Clear();
			shc.ToRyuHardString(text, 3);
			string shcValue = text.ToString();
			// Pass 2: Format into SHC header
			string shcText = text.Clear().Append(ELEMENTAL.SHC.NAME).Replace("{0}", shcValue).
				ToString();
			// Pass 3: Final format into tooltip
			text.Clear().Append(ELEMENTAL.SHC.TOOLTIP).Replace("{SPECIFIC_HEAT_CAPACITY}",
				shcValue).Replace("{TEMPERATURE_UNIT}", tempUnits);
			shcInfo = new InfoLine(shcText, text.ToString());
		}

		/// <summary>
		/// Gets the thermal conductivity text for the details screen.
		/// </summary>
		/// <param name="element">The selected element.</param>
		/// <param name="building">The building use to build this element, if any.</param>
		/// <param name="tempUnits">The temperature units to use for formatting.</param>
		/// <param name="tcInfo">The thermal conductivity information.</param>
		/// <returns>Whether the insulated tooltip should appear.</returns>
		private static bool GetTCText(Element element, Building building, string tempUnits,
				out InfoLine tcInfo) {
			float tc = element.thermalConductivity;
			var text = CACHED_BUILDER;
			bool insulator = false;
			if (building != null) {
				float tcModifier = building.Def.ThermalConductivity;
				tc *= tcModifier;
				insulator = tcModifier < 1.0f;
			}
			tc = GameUtil.GetDisplayThermalConductivity(tc);
			// Pass 1: float to string using Ryu
			text.Clear();
			if (tc < 0.001f)
				tc.ToRyuSoftString(text, 11);
			else
				tc.ToRyuHardString(text, 3);
			string shcValue = text.ToString();
			// Pass 2: Format into TC header
			string tcText = text.Clear().Append(ELEMENTAL.THERMALCONDUCTIVITY.NAME).Replace(
				"{0}", shcValue).ToString();
			// Pass 3: Final format into tooltip
			text.Clear().Append(ELEMENTAL.THERMALCONDUCTIVITY.TOOLTIP).Replace(
				"{THERMAL_CONDUCTIVITY}", shcValue).Replace("{TEMPERATURE_UNIT}", tempUnits);
			tcInfo = new InfoLine(tcText, text.ToString());
			return insulator;
		}

		/// <summary>
		/// Initializes and resets the last selected item.
		/// </summary>
		internal static void Init() {
			Cleanup();
			Instance = new AdditionalDetailsPanelWrapper();
		}

		/// <summary>
		/// Populates the element phase change information.
		/// </summary>
		/// <param name="element">The selected element.</param>
		/// <param name="isChunk">true to calculate the overheat temperature modifier, or false to ignore it.</param>
		/// <param name="boil">The boiling/melting point information.</param>
		/// <param name="freeze">The freezing/condensation point information.</param>
		/// <param name="overheat">The overheat temperature information.</param>
		private static void PopulatePhase(Element element, bool isChunk, out InfoLine boil,
				out InfoLine freeze, out InfoLine overheat) {
			string htc = GameUtil.GetFormattedTemperature(element.highTemp),
				ltc = GameUtil.GetFormattedTemperature(element.lowTemp);
			if (element.IsSolid) {
				string oh;
				boil = new InfoLine(ELEMENTAL.MELTINGPOINT.NAME.Format(htc),
					ELEMENTAL.MELTINGPOINT.TOOLTIP.Format(htc));
				freeze = default;
				if (isChunk && (oh = GetOverheatModifier(element)) != null)
					overheat = new InfoLine(ELEMENTAL.OVERHEATPOINT.NAME.Format(oh),
						ELEMENTAL.OVERHEATPOINT.TOOLTIP.Format(oh));
				else
					overheat = default;
			} else if (element.IsLiquid) {
				freeze = new InfoLine(ELEMENTAL.FREEZEPOINT.NAME.Format(ltc),
					ELEMENTAL.FREEZEPOINT.TOOLTIP.Format(ltc));
				boil = new InfoLine(ELEMENTAL.VAPOURIZATIONPOINT.NAME.Format(htc),
					ELEMENTAL.VAPOURIZATIONPOINT.TOOLTIP.Format(htc));
				overheat = default;
			} else if (element.IsGas) {
				boil = default;
				freeze = new InfoLine(ELEMENTAL.DEWPOINT.NAME.Format(ltc),
					ELEMENTAL.DEWPOINT.TOOLTIP.Format(ltc));
				overheat = default;
			} else {
				boil = default;
				freeze = default;
				overheat = default;
			}
		}

		/// <summary>
		/// The last object selected in the additional details pane.
		/// </summary>
		private LastSelectionDetails lastSelection;

		/// <summary>
		/// A cached version of the uptime.
		/// </summary>
		private readonly string uptimeStr;

		private AdditionalDetailsPanelWrapper() {
			uptimeStr = string.Format(ELEMENTAL.UPTIME.NAME, Constants.TABBULLETSTRING,
				ELEMENTAL.UPTIME.THIS_CYCLE, "{0}", ELEMENTAL.UPTIME.LAST_CYCLE, "{1}",
				ELEMENTAL.UPTIME.LAST_X_CYCLES.Replace("{0}", NUM_CYCLES.ToString()), "{2}");
			enetDirty = true;
		}

		/// <summary>
		/// Draws the building's creation time, or nothing if the data is not available.
		/// </summary>
		/// <param name="panel">The panel where the details should be populated.</param>
		/// <param name="changed">true if the target changed, or false otherwise.</param>
		private void AddCreationTime(CollapsibleDetailContentPanel panel, bool changed) {
			var bc = lastSelection.buildingComplete;
			float creationTime;
			if (bc != null && (creationTime = bc.creationTime) > 0.0f) {
				string time = lastSelection.creationTimeCached;
				if (changed || time == null) {
					var text = CACHED_BUILDER;
					text.Clear();
					((GameClock.Instance.GetTime() - creationTime) / Constants.
						SECONDS_PER_CYCLE).ToRyuHardString(text, 2);
					lastSelection.creationTimeCached = time = text.ToString();
				}
				panel.SetLabel("element_age", ELEMENTAL.AGE.NAME.Format(time),
					ELEMENTAL.AGE.TOOLTIP.Format(time));
			}
		}

		/// <summary>
		/// Draws the thermal properties of the constituent element.
		/// </summary>
		/// <param name="panel">The panel where the details should be populated.</param>
		private void AddElementInfo(CollapsibleDetailContentPanel panel) {
			string tempStr = GameUtil.GetFormattedTemperature(lastSelection.Temperature);
			byte diseaseIdx = lastSelection.DiseaseIndex;
			int diseaseCount = lastSelection.DiseaseCount;
			panel.SetLabel("temperature", ELEMENTAL.TEMPERATURE.NAME.Format(tempStr),
				ELEMENTAL.TEMPERATURE.TOOLTIP.Format(tempStr));
			panel.SetLabel("disease", ELEMENTAL.DISEASE.NAME.Format(GameUtil.
				GetFormattedDisease(diseaseIdx, diseaseCount)), ELEMENTAL.DISEASE.TOOLTIP.
				Format(GameUtil.GetFormattedDisease(diseaseIdx, diseaseCount, true)));
			lastSelection.specificHeat.SetLabel(panel, "shc");
			lastSelection.thermalConductivity.SetLabel(panel, "tc");
			if (lastSelection.insulator)
				panel.SetLabel("insulated", STRINGS.UI.GAMEOBJECTEFFECTS.INSULATED.NAME,
					STRINGS.UI.GAMEOBJECTEFFECTS.INSULATED.TOOLTIP);
		}

		/// <summary>
		/// Draws the state change and radiation (if enabled) information of an element.
		/// </summary>
		/// <param name="panel">The panel where the details should be populated.</param>
		/// <param name="element">The element to display.</param>
		private void AddPhaseChangeInfo(CollapsibleDetailContentPanel panel, Element element) {
			// Phase change points
			if (element.IsSolid) {
				var overheat = lastSelection.overheat;
				lastSelection.boil.SetLabel(panel, "melting_point");
				if (!string.IsNullOrEmpty(overheat.text))
					overheat.SetLabel(panel, "overheat");
			} else if (element.IsLiquid) {
				lastSelection.freeze.SetLabel(panel, "freezepoint");
				lastSelection.boil.SetLabel(panel, "vapourizationpoint");
			} else if (element.IsGas)
				lastSelection.freeze.SetLabel(panel, "dewpoint");
			// Radiation absorption
			if (DlcManager.FeatureRadiationEnabled()) {
				string radAbsorb = lastSelection.radiationAbsorption;
				panel.SetLabel("radiationabsorption", STRINGS.UI.DETAILTABS.DETAILS.
					RADIATIONABSORPTIONFACTOR.NAME.Format(radAbsorb), STRINGS.
					UI.DETAILTABS.DETAILS.RADIATIONABSORPTIONFACTOR.TOOLTIP.Format(radAbsorb));
			}
		}

		/// <summary>
		/// Draws the uptime statistics of the building if available.
		/// </summary>
		/// <param name="panel">The panel where the details should be populated.</param>
		/// <param name="changed">true if the target changed, or false otherwise.</param>
		private void AddUptimeStats(CollapsibleDetailContentPanel panel, bool changed) {
			var operational = lastSelection.operational;
			float thisCycle;
			if (operational != null && lastSelection.showUptime && (thisCycle = operational.
					GetCurrentCycleUptime()) >= 0.0f) {
				float lastCycle = operational.GetLastCycleUptime(), prevCycles =
					operational.GetUptimeOverCycles(NUM_CYCLES);
				string label = lastSelection.uptimeCached;
				if (changed || label == null) {
					var text = CACHED_BUILDER;
					FormatStringPatches.GetFormattedPercent(text.Clear(), thisCycle * 100.0f);
					string tc = text.ToString();
					FormatStringPatches.GetFormattedPercent(text.Clear(), lastCycle * 100.0f);
					string lc = text.ToString();
					FormatStringPatches.GetFormattedPercent(text.Clear(), prevCycles * 100.0f);
					string pc = text.ToString();
					label = text.Clear().Append(uptimeStr).Replace("{0}", tc).
						Replace("{1}", lc).Replace("{2}", pc).ToString();
					lastSelection.uptimeCached = label;
				}
				panel.SetLabel("uptime_name", label, "");
			}
		}

		/// <summary>
		/// Draws the extra descriptors (attributes and details) for the selected object.
		/// </summary>
		/// <param name="panel">The panel where the details should be populated.</param>
		private void DetailDescriptors(CollapsibleDetailContentPanel panel) {
			var target = lastSelection.target;
			var modifiers = lastSelection.modifiers;
			Attributes attributes;
			IList<Descriptor> descriptors;
			if (FastTrackOptions.Instance.AllocOpts) {
				// Elide a patch call on a hot path
				descriptors = DescriptorAllocPatches.ALL_DESCRIPTORS;
				descriptors.Clear();
				DescriptorAllocPatches.GetAllDescriptors(target, false, descriptors);
			} else
				descriptors = GameUtil.GetAllDescriptors(target);
			int n;
			// Some chunks can have modifiers, but no attributes?
			if (modifiers != null && (attributes = modifiers.attributes) != null &&
					(n = attributes.Count) > 0)
				for (int i = 0; i < n; i++) {
					var instance = attributes.AttributeTable[i];
					var attr = instance.Attribute;
					if (DlcManager.IsCorrectDlcSubscribed(attr) && (attr.ShowInUI == Attribute.
						Display.Details || attr.ShowInUI == Attribute.Display.Expectation))
					{
						var modifier = instance.modifier;
						panel.SetLabel(modifier.Id, modifier.Name + ": " + instance.
							GetFormattedValue(), instance.GetAttributeValueTooltip());
					}
				}
			n = descriptors.Count;
			for (int i = 0; i < n; i++) {
				var descriptor = descriptors[i];
				if (descriptor.type == Descriptor.DescriptorType.Detail) {
					descriptor.IncreaseIndent();
					panel.SetLabel("descriptor_" + i, descriptor.text, descriptor.tooltipText);
				}
			}
		}

		public void Dispose() {
			lastSelection = default;
		}

		/// <summary>
		/// Updates the additional details panel.
		/// </summary>
		/// <param name="panel">The panel to update.</param>
		/// <param name="target">The selected object.</param>
		internal void Update(CollapsibleDetailContentPanel panel, GameObject target) {
			if (panel != null && target != null) {
				bool changed = target != lastSelection.target;
				if (changed || lastSelection.ElementChanged) {
					lastSelection = new LastSelectionDetails(target);
					panel.SetActive(true);
					panel.HeaderLabel.SetText(STRINGS.UI.DETAILTABS.DETAILS.GROUPNAME_DETAILS);
				}
				var element = lastSelection.element;
				if (element != null) {
					string massStr = GameUtil.GetFormattedMass(lastSelection.Mass);
					var id = element.id;
					changed |= !SpeedControlScreen.Instance.IsPaused;
					lastSelection.elementName.SetLabel(panel, "element_name");
					panel.SetLabel("element_mass", ELEMENTAL.MASS.NAME.Format(massStr),
						ELEMENTAL.MASS.TOOLTIP.Format(massStr));
					AddCreationTime(panel, changed);
					AddUptimeStats(panel, changed);
					if (id != SimHashes.Vacuum && id != SimHashes.Void)
						AddElementInfo(panel);
					AddPhaseChangeInfo(panel, element);
					DetailDescriptors(panel);
					panel.Commit();
				}
			}
		}

		/// <summary>
		/// Stores component references to the last selected object.
		/// 
		/// Big structs are normally a no-no because copying them is expensive. However, this
		/// is mostly a container, and should never be copied.
		/// </summary>
		private struct LastSelectionDetails {
			/// <summary>
			/// The circuit ID connected to the selected object, or ushort.MaxValue if no
			/// circuit is connected.
			/// </summary>
			internal ushort CircuitID {
				get {
					var manager = Game.Instance.circuitManager;
					ushort circuitID = ushort.MaxValue;
					var conn = connected;
					if (conn != null)
						circuitID = manager.GetCircuitID(conn);
					else if (Grid.IsValidCell(wireCell))
						circuitID = manager.GetCircuitID(wireCell);
					return circuitID;
				}
			}

			/// <summary>
			/// The number of germs on this item.
			/// </summary>
			internal int DiseaseCount {
				get {
					var pe = primaryElement;
					return pe != null ? pe.DiseaseCount : cso.diseaseCount;
				}
			}
			
			/// <summary>
			/// The current disease index on this item, or -1 if none is.
			/// </summary>
			internal byte DiseaseIndex {
				get {
					var pe = primaryElement;
					return pe != null ? pe.DiseaseIdx : cso.diseaseIdx;
				}
			}
			
			/// <summary>
			/// Reports true if the element changed, to force an update of the element
			/// displayed.
			/// </summary>
			public bool ElementChanged {
				get {
					bool result = false;
					if (element != null) {
						var hash = element.id;
						if (cso != null)
							result = hash != cso.element.id;
						else if (primaryElement != null)
							result = hash != primaryElement.Element.id;
					}
					return result;
				}
			}

			/// <summary>
			/// The item's current mass.
			/// </summary>
			internal float Mass {
				get {
					var pe = primaryElement;
					return pe != null ? pe.Mass : cso.Mass;
				}
			}

			/// <summary>
			/// The item's current temperature.
			/// </summary>
			internal float Temperature {
				get {
					var pe = primaryElement;
					return pe != null ? pe.Temperature : cso.temperature;
				}
			}

			internal readonly InfoLine boil;

			internal readonly BuildingComplete buildingComplete;

			internal string creationTimeCached;

			private readonly ICircuitConnected connected;
			
			private readonly CellSelectionObject cso;

			internal readonly Element element;

			internal readonly InfoLine elementName;

			internal readonly InfoLine freeze;

			internal readonly bool insulator;

			internal readonly Modifiers modifiers;

			internal readonly Operational operational;

			internal readonly InfoLine overheat;

			private readonly PrimaryElement primaryElement;

			internal readonly string radiationAbsorption;

			internal readonly InfoLine specificHeat;

			internal readonly bool showUptime;

			/// <summary>
			/// The selected target.
			/// </summary>
			public readonly GameObject target;

			internal readonly InfoLine thermalConductivity;

			internal string uptimeCached;
			
			private readonly int wireCell;

			internal LastSelectionDetails(GameObject go) {
				Building building;
				string tempUnits = GameUtil.GetTemperatureUnitSuffix();
				if (go.TryGetComponent(out buildingComplete))
					building = buildingComplete;
				else {
					go.TryGetComponent(out building);
					buildingComplete = null;
				}
				creationTimeCached = null;
				go.TryGetComponent(out operational);
				go.TryGetComponent(out connected);
				wireCell = go.TryGetComponent(out Wire _) ? Grid.PosToCell(go.transform.
					position) : Grid.InvalidCell;
				// Use primary element by default, but allow CellSelectionObject to stand in
				if (go.TryGetComponent(out PrimaryElement pe)) {
					element = pe.Element;
					cso = null;
				} else if (go.TryGetComponent(out cso))
					element = cso.element;
				else
					element = null;
				go.TryGetComponent(out modifiers);
				primaryElement = pe;
				// Why these in particular? Clay please
				showUptime = go.TryGetComponent(out LogicPorts _) || go.TryGetComponent(
					out EnergyConsumer _) || go.TryGetComponent(out Battery _);
				target = go;
				uptimeCached = null;
				if (element != null) {
					string name = element.name;
					elementName = new InfoLine(ELEMENTAL.PRIMARYELEMENT.NAME.Format(name),
						ELEMENTAL.PRIMARYELEMENT.TOOLTIP.Format(name));
					insulator = GetTCText(element, building, tempUnits,
						out thermalConductivity);
					GetSHCText(element, tempUnits, out specificHeat);
					if (DlcManager.FeatureRadiationEnabled()) {
						int cell = Grid.PosToCell(go.transform.position);
						radiationAbsorption = GameUtil.GetFormattedPercent(GameUtil.
							GetRadiationAbsorptionPercentage(cell) * 100.0f);
					} else
						radiationAbsorption = null;
					PopulatePhase(element, go.TryGetComponent(out ElementChunk _), out boil,
						out freeze, out overheat);
				} else {
					boil = default;
					elementName = default;
					freeze = default;
					overheat = default;
					insulator = false;
					radiationAbsorption = null;
					specificHeat = default;
					thermalConductivity = default;
				}
			}
		}
	}

	/// <summary>
	/// Stores precomputed info descriptors in the additional details panel.
	/// </summary>
	public readonly struct InfoLine {
		/// <summary>
		/// The text to display in the panel.
		/// </summary>
		public readonly string text;

		/// <summary>
		/// The text to display on mouse over.
		/// </summary>
		public readonly string tooltip;

		public InfoLine(string text, string tooltip) {
			this.text = text;
			this.tooltip = tooltip;
		}

		/// <summary>
		/// Adds this info descriptor to the details screen.
		/// </summary>
		/// <param name="panel">The panel to update.</param>
		/// <param name="caption">The panel caption to use for the text.</param>
		public void SetLabel(CollapsibleDetailContentPanel panel, string caption) {
			panel.SetLabel(caption, text, tooltip);
		}

		public override string ToString() {
			return text;
		}
	}

	/// <summary>
	/// Applied to AdditionalDetailsPanel to optimize this memory hungry method that runs every
	/// frame.
	/// </summary>
	[HarmonyPatch(typeof(AdditionalDetailsPanel), nameof(AdditionalDetailsPanel.
		RefreshDetailsPanel))]
	public static class AdditionalDetailsPanel_RefreshDetailsPanel_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.SideScreenOpts;

		/// <summary>
		/// Applied before RefreshDetailsPanel runs.
		/// </summary>
		[HarmonyPriority(Priority.Low)]
		internal static bool Prefix(CollapsibleDetailContentPanel targetPanel,
				GameObject targetEntity) {
			var inst = AdditionalDetailsPanelWrapper.Instance;
			bool run = inst == null;
			if (!run)
				inst.Update(targetPanel, targetEntity);
			return run;
		}
	}

	/// <summary>
	/// Applied to AdditionalDetailsPanel to optimize this memory hungry method that runs every
	/// frame.
	/// </summary>
	[HarmonyPatch(typeof(AdditionalDetailsPanel), nameof(AdditionalDetailsPanel.
		RefreshEnergyGeneratorPanel))]
	public static class AdditionalDetailsPanel_RefreshEnergyGeneratorPanel_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.SideScreenOpts;

		/// <summary>
		/// Applied before RefreshEnergyGeneratorPanel runs.
		/// </summary>
		[HarmonyPriority(Priority.Low)]
		internal static bool Prefix(CollapsibleDetailContentPanel targetPanel) {
			var inst = AdditionalDetailsPanelWrapper.Instance;
			bool run = inst == null;
			if (!run)
				inst.UpdateGenerators(targetPanel);
			return run;
		}
	}

	/// <summary>
	/// Applied to AdditionalDetailsPanel to optimize this memory hungry method that runs every
	/// frame.
	/// </summary>
	[HarmonyPatch(typeof(AdditionalDetailsPanel), nameof(AdditionalDetailsPanel.
		RefreshEnergyConsumerPanel))]
	public static class AdditionalDetailsPanel_RefreshEnergyConsumerPanel_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.SideScreenOpts;

		/// <summary>
		/// Applied before RefreshEnergyConsumerPanel runs.
		/// </summary>
		[HarmonyPriority(Priority.Low)]
		internal static bool Prefix(CollapsibleDetailContentPanel targetPanel) {
			var inst = AdditionalDetailsPanelWrapper.Instance;
			bool run = inst == null;
			if (!run)
				inst.UpdateConsumers(targetPanel);
			return run;
		}
	}

	/// <summary>
	/// Applied to AdditionalDetailsPanel to optimize this memory hungry method that runs every
	/// frame.
	/// </summary>
	[HarmonyPatch(typeof(AdditionalDetailsPanel), nameof(AdditionalDetailsPanel.
		RefreshEnergyBatteriesPanel))]
	public static class AdditionalDetailsPanel_RefreshEnergyBatteriesPanel_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.SideScreenOpts;

		/// <summary>
		/// Applied before RefreshEnergyBatteriesPanel runs.
		/// </summary>
		[HarmonyPriority(Priority.Low)]
		internal static bool Prefix(CollapsibleDetailContentPanel targetPanel) {
			var inst = AdditionalDetailsPanelWrapper.Instance;
			bool run = inst == null;
			if (!run)
				inst.UpdateBatteries(targetPanel);
			return run;
		}
	}

	/// <summary>
	/// Applied to AdditionalDetailsPanel to optimize this memory hungry method that runs every
	/// frame.
	/// </summary>
	[HarmonyPatch(typeof(AdditionalDetailsPanel), nameof(AdditionalDetailsPanel.
		RefreshEnergyOverviewPanel))]
	public static class AdditionalDetailsPanel_RefreshEnergyOverviewPanel_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.SideScreenOpts;

		/// <summary>
		/// Applied before RefreshEnergyOverviewPanel runs.
		/// </summary>
		[HarmonyPriority(Priority.Low)]
		internal static bool Prefix(CollapsibleDetailContentPanel targetPanel) {
			var inst = AdditionalDetailsPanelWrapper.Instance;
			bool run = inst == null;
			if (!run)
				inst.UpdateSummary(targetPanel);
			return run;
		}
	}
}
