using System;
using System.Linq;
using System.Reflection;
using Colossal.UI.Binding;
using Game;
using Game.City;
using Game.Prefabs;
using Game.Simulation;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;
using UnityEngine;

namespace TollPlaza
{
    /// <summary>
    /// A "Toll Plazas" line under Income on the game's Budget screen, counted in its total income and monthly
    /// balance (and the budget chart). The tolls themselves are paid into the city's money as vehicles pass
    /// (TollCollectionSystem); this only shows them.
    ///
    /// The game has a fixed list of income sources, so this adds one more item to the budget screen's list
    /// (UIEconomyConfigurationPrefab, in memory only) with source index 14 (IncomeSource.Count), and wraps
    /// two of BudgetUISystem's bindings (private fields, found by reflection): the per-source values get a
    /// 15th value, and the total income gets the tolls added. If the game changes any of this, it logs a
    /// warning and the Budget screen stays as it was. Nothing is saved.
    /// </summary>
    public partial class TollBudgetSystem : GameSystemBase
    {
        public const string ItemId = "TollPlazas";

        // The source index of the toll line in the budget values (the game's own sources are 0..13).
        private const int TollSource = (int)IncomeSource.Count;

        private PrefabSystem m_PrefabSystem;
        private TimeSystem m_TimeSystem;
        private BudgetUISystem m_BudgetUISystem;
        private ICityServiceBudgetSystem m_CityServiceBudgetSystem;
        private EntityQuery m_ConfigQuery;
        private bool m_Hooked;
        private bool m_Failed;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_BudgetUISystem = World.GetOrCreateSystemManaged<BudgetUISystem>();
            m_CityServiceBudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_ConfigQuery = GetEntityQuery(ComponentType.ReadOnly<UIEconomyConfigurationData>());
        }

        protected override void OnUpdate()
        {
            if (m_Hooked || m_Failed || m_ConfigQuery.IsEmptyIgnoreFilter)
            {
                return;
            }
            try
            {
                Hook();
                m_Hooked = true;
                Mod.Log.Info("[TollPlaza] Budget screen: \"Toll Plazas\" income line added");
            }
            catch (Exception e)
            {
                m_Failed = true;
                Mod.Log.Warn(e, "[TollPlaza] Budget screen line not added (the game's budget code has changed)");
            }
        }

        /// <summary>
        /// The toll income for the budget, in dollars a month: last month's tolls, or this month's so far
        /// until a month with tolls has passed (the game's other lines are monthly estimates too).
        /// </summary>
        public int MonthlyTolls()
        {
            PlazaStats.City.Roll(PlazaStats.MonthKey(m_TimeSystem));
            long cents = PlazaStats.City.LastMonth.Cents > 0 ? PlazaStats.City.LastMonth.Cents : PlazaStats.City.ThisMonth.Cents;
            return (int)(cents / 100);
        }

        private void Hook()
        {
            // The list item: shown with the game's own row, chart colour and details panel.
            UIEconomyConfigurationPrefab config = m_PrefabSystem.GetSingletonPrefab<UIEconomyConfigurationPrefab>(m_ConfigQuery);
            if (!config.m_IncomeItems.Any(item => item.m_ID == ItemId))
            {
                var item = new BudgetItem<IncomeSource>
                {
                    m_ID = ItemId,
                    m_Color = new Color(0.36f, 0.2f, 0.55f),
                    m_Icon = Mod.IconUri,
                    m_Sources = new[] { (IncomeSource)TollSource },
                };
                config.m_IncomeItems = config.m_IncomeItems.Append(item).ToArray();
            }

            // The per-source values: the game's 14, then the tolls.
            RawValueBinding values = Field<RawValueBinding>(m_BudgetUISystem, "m_IncomeValuesBinding");
            SetField(values, typeof(RawValueBinding), "m_WriterDelegate", new Action<IJsonWriter>(writer =>
            {
                writer.ArrayBegin((uint)TollSource + 1);
                for (int i = 0; i < TollSource; i++)
                {
                    writer.Write(m_CityServiceBudgetSystem.GetIncome((IncomeSource)i));
                }
                writer.Write(MonthlyTolls());
                writer.ArrayEnd();
            }));

            // The total income (also used for the monthly balance): the game's total plus the tolls.
            GetterValueBinding<int> total = Field<GetterValueBinding<int>>(m_BudgetUISystem, "m_TotalIncomeBinding");
            SetField(total, typeof(GetterValueBinding<int>), "m_Getter",
                new Func<int>(() => m_CityServiceBudgetSystem.GetTotalIncome() + MonthlyTolls()));

            // Resend the list in case the Budget screen was already opened.
            Field<RawValueBinding>(m_BudgetUISystem, "m_IncomeItemsBinding").Update();
        }

        private static T Field<T>(object owner, string name) where T : class
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(owner.GetType().Name, name);
            return field.GetValue(owner) as T ?? throw new InvalidCastException($"{owner.GetType().Name}.{name} is not a {typeof(T).Name}");
        }

        private static void SetField(object owner, Type type, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(type.Name, name);
            field.SetValue(owner, value);
        }
    }
}
