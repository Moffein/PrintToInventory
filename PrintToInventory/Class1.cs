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
	[BepInPlugin("com.Moffein.PrintToInventory", "PrintToInventory", "1.3.0")]

    public class PrintToInventory : BaseUnityPlugin
    {
		//private static SceneDef bazaarDef = Addressables.LoadAssetAsync<SceneDef>("RoR2/Base/bazaar/bazaar.asset").WaitForCompletion();
		public static bool affectScrappers = true;
		public static bool affectPrinters = true;
		public static bool affectCauldrons = true;
		public static bool affectCleanse = true;

		public static bool multiplayerOnly = true;

		public void Awake()
        {
			if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.funkfrog_sipondo.sharesuite"))
            {
				Debug.LogError("PrintToInventory: ShareSuite detected! PrintToInventory will be disabled due to incompatibilities.");
				return;
			}
			
			multiplayerOnly = Config.Bind("General", "Multiplayer Only", true, "Only active this mod in multiplayer.").Value;
            affectCauldrons = Config.Bind("General", "Cauldron", true, "Affect this interactable.").Value;
			affectPrinters = Config.Bind("General", "Printer", true, "Affect this interactable.").Value;
			affectScrappers = Config.Bind("General", "Scrapper", true, "Affect this interactable.").Value;
			affectCleanse = Config.Bind("General", "Cleansing Pool", true, "Affect this interactable.").Value;

            On.RoR2.Run.Start += Run_Start;
            On.RoR2.Run.OnDestroy += Run_OnDestroy;
		}

        private void Run_OnDestroy(On.RoR2.Run.orig_OnDestroy orig, Run self)
        {
			orig(self);
			RemoveHooks();
        }

        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
			orig(self);
			if (!multiplayerOnly || !RoR2Application.isInSinglePlayer)
			{
				AddHooks();
			}
        }

        private static bool hooksAdded = false;
		public void AddHooks()
		{
			if (hooksAdded) return;
			hooksAdded = true;

            if (affectPrinters) On.EntityStates.Duplicator.Duplicating.OnEnter += Duplicating_OnEnter;

            if (affectCauldrons || affectCleanse)
            {
                On.RoR2.PurchaseInteraction.OnInteractionBegin += ImmediatelyGiveItem;
                On.RoR2.ShopTerminalBehavior.DropPickup += PreventDrop;
            }
            if (affectScrappers)
            {
                IL.EntityStates.Scrapper.ScrappingToIdle.OnEnter += ScrappingToIdle_DirectToInventory;
            }
        }

		public void RemoveHooks()
		{
			if (!hooksAdded) return;
			hooksAdded = false;

            if (affectPrinters) On.EntityStates.Duplicator.Duplicating.OnEnter -= Duplicating_OnEnter;

            if (affectCauldrons || affectCleanse)
            {
                On.RoR2.PurchaseInteraction.OnInteractionBegin -= ImmediatelyGiveItem;
                On.RoR2.ShopTerminalBehavior.DropPickup -= PreventDrop;
            }
            if (affectScrappers)
            {
                IL.EntityStates.Scrapper.ScrappingToIdle.OnEnter -= ScrappingToIdle_DirectToInventory;
            }
        }

        private void ScrappingToIdle_DirectToInventory(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(MoveType.After, x => x.MatchCallvirt<ScrapperController>("IsRewardPickupQueueEmpty")))
            {
                c.Emit(OpCodes.Ldarg_0);
				c.EmitDelegate<Func<bool, EntityStates.Scrapper.ScrappingToIdle, bool>>((queueEmpty, self) =>
				{
					if (!queueEmpty && self.scrapperController && self.scrapperController.interactor && self.scrapperController.pickupPrintQueue != null)
					{
						CharacterBody cb = self.scrapperController.interactor.GetComponent<CharacterBody>();
						if (cb && cb.inventory)
						{
							List<UniquePickup> processedPickups = new List<UniquePickup>();
							foreach (UniquePickup pickup in self.scrapperController.pickupPrintQueue)
							{
								PickupDef pd = PickupCatalog.GetPickupDef(pickup.pickupIndex);
								if (pd != null && pd.itemIndex != ItemIndex.None)
								{
									cb.inventory.GiveItemPermanent(pd.itemIndex);
									processedPickups.Add(pickup);
								}
							}

							foreach (UniquePickup up in processedPickups)
							{
									self.scrapperController.pickupPrintQueue.Remove(up);
							}

							return self.scrapperController.IsRewardPickupQueueEmpty();
						}
					}
					return queueEmpty;
                });
            }
            else
            {
                Debug.LogError("PrintToInventory: Scrapper IL hook failed.");
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
						PickupDef pd = PickupCatalog.GetPickupDef(stb.pickup.pickupIndex);
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
						PickupDef pd = PickupCatalog.GetPickupDef(stb.pickup.pickupIndex);
						if (pd != null && pd.itemIndex != ItemIndex.None)
						{
							int dropCount = 1;

							//hardcoded fix for red to white cauldrons
							//lunar is for Eulogy
							if (isCauldron && pi.cost == 1 && pi.costType == CostTypeIndex.RedItem && (stb.itemTier == ItemTier.Tier1 || stb.itemTier == ItemTier.VoidTier1 || stb.itemTier == ItemTier.Lunar))
							{
                                dropCount = 3;
                            }

							cb.inventory.GiveItem(pd.itemIndex, dropCount);
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
