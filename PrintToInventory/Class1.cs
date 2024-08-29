using BepInEx;
using EntityStates;
using EntityStates.Scrapper;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace R2API.Utils
{
	[AttributeUsage(AttributeTargets.Assembly)]
	public class ManualNetworkRegistrationAttribute : Attribute
	{
	}
}

namespace PrintToInventory
{
	[BepInDependency("com.funkfrog_sipondo.sharesuite", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInPlugin("com.Moffein.PrintToInventory", "PrintToInventory", "1.1.2")]

    public class PrintToInventory : BaseUnityPlugin
    {
		//private static SceneDef bazaarDef = Addressables.LoadAssetAsync<SceneDef>("RoR2/Base/bazaar/bazaar.asset").WaitForCompletion();
		public static bool affectScrappers = true;
		public static bool affectPrinters = true;
		public static bool affectCauldrons = true;
		public static bool affectCleanse = true;

		public void Awake()
        {
			if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.funkfrog_sipondo.sharesuite"))
            {
				Debug.LogError("PrintToInventory: ShareSuite detected! PrintToInventory will be disabled due to incompatibilities.");
				return;
			}

			affectCauldrons = Config.Bind("General", "Cauldron", true, "Affect this interactable.").Value;
			affectPrinters = Config.Bind("General", "Printer", true, "Affect this interactable.").Value;
			affectScrappers = Config.Bind("General", "Scrapper", true, "Affect this interactable.").Value;
			affectCleanse = Config.Bind("General", "Cleansing Pool", true, "Affect this interactable.").Value;

			if (affectPrinters) On.EntityStates.Duplicator.Duplicating.OnEnter += Duplicating_OnEnter;

			if (affectCauldrons || affectCleanse)
            {
				On.RoR2.PurchaseInteraction.OnInteractionBegin += ImmediatelyGiveItem;
				On.RoR2.ShopTerminalBehavior.DropPickup += PreventDrop;
			}
			if (affectScrappers)
            {
				IL.EntityStates.Scrapper.ScrappingToIdle.OnEnter += (il) =>
				{
					ILCursor c = new ILCursor(il);
					c.GotoNext(x => x.MatchLdloc(0), x => x.MatchLdsfld(typeof(PickupIndex), "none"));
					c.Index++;
					c.Emit(OpCodes.Ldarg_0);
					c.EmitDelegate<Func<PickupIndex, ScrappingToIdle, PickupIndex>>((pickupIndex, self) =>
					{
						if (pickupIndex != PickupIndex.none && self.scrapperController && self.scrapperController.interactor)
                        {
							CharacterBody cb = self.scrapperController.interactor.GetComponent<CharacterBody>();
							if (cb && cb.inventory)
							{
								PickupDef pd = PickupCatalog.GetPickupDef(pickupIndex);
								if (pd != null && pd.itemIndex != ItemIndex.None)
								{
									cb.inventory.GiveItem(pd.itemIndex, self.scrapperController.itemsEaten);
									self.scrapperController.itemsEaten = 0;
									return PickupIndex.none;
								}
							}
						}
						return pickupIndex;
					});
				};
            }
		}

		private void Duplicating_OnEnter(On.EntityStates.Duplicator.Duplicating.orig_OnEnter orig, EntityStates.Duplicator.Duplicating self)
		{
			orig(self);
			if (NetworkServer.active)
			{
				PurchaseInteraction pi = self.GetComponent<PurchaseInteraction>();
				ShopTerminalBehavior stb = self.GetComponent<ShopTerminalBehavior>();
				if (pi && stb)
				{
					CharacterBody cb = pi.lastActivator.GetComponent<CharacterBody>();
					if (cb && cb.inventory)
					{
						PickupDef pd = PickupCatalog.GetPickupDef(stb.pickupIndex);
						if (pd != null && pd.itemIndex != ItemIndex.None)
						{
							cb.inventory.GiveItem(pd.itemIndex, 1);
							stb.SetHasBeenPurchased(true);

							self.hasDroppedDroplet = true;
							self.hasStartedCooking = true;
						}
					}
				}
			}
		}

		private void ImmediatelyGiveItem(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
        {
			orig(self, activator);
			PurchaseInteraction pi = self.GetComponent<PurchaseInteraction>();

			if (pi)
            {
				bool isCauldron = affectCauldrons && pi.displayNameToken == "BAZAAR_CAULDRON_NAME";    //Matching by token is bad.
				bool isCleanse = affectCleanse && pi.displayNameToken == "SHRINE_CLEANSE_NAME";    //Matching by token is bad.

				if (isCauldron || isCleanse)
				{
					CharacterBody cb = activator.GetComponent<CharacterBody>();
					ShopTerminalBehavior stb = self.GetComponent<ShopTerminalBehavior>();

					if (cb && cb.inventory && stb)
					{
						PickupDef pd = PickupCatalog.GetPickupDef(stb.pickupIndex);
						if (pd != null && pd.itemIndex != ItemIndex.None)
						{
							cb.inventory.GiveItem(pd.itemIndex, 1);
							stb.SetHasBeenPurchased(true);
							pi.lastActivator = null;
						}
					}
				}
			}
		}

        private void PreventDrop(On.RoR2.ShopTerminalBehavior.orig_DropPickup orig, ShopTerminalBehavior self)
        {
			if (NetworkServer.active)
			{
				PurchaseInteraction pi = self.GetComponent<PurchaseInteraction>();
				if (pi)
				{
					bool isCauldron = affectCauldrons && pi.displayNameToken == "BAZAAR_CAULDRON_NAME";    //Matching by token is bad.
					bool isCleanse = affectCleanse && pi.displayNameToken == "SHRINE_CLEANSE_NAME";    //Matching by token is bad.

					if ((isCauldron || isCleanse) && pi.lastActivator == null)
					{
						return;
					}
				}
			}
			orig(self);
        }
    }
}
