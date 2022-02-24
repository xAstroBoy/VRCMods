using System;
using System.Collections;
using System.Collections.Generic;
using FavCat.Adapters;
using FavCat.CustomLists;
using FavCat.Database.Stored;
using MelonLoader;
using UIExpansionKit.API;
using UIExpansionKit.API.Controls;
using UIExpansionKit.Components;
using UnityEngine;
using UnityEngine.UI;
using VRC.Core;
using VRC.UI;

namespace FavCat.Modules
{
    public class WorldsModule : ExtendedFavoritesModuleBase<StoredWorld>
    {
        private readonly PageWorldInfo myPageWorldInfo;
        
        public WorldsModule() : base(ExpandedMenu.WorldMenu, FavCatMod.Database.WorldFavorites, GetListsParent(), true, true)
        {
            ExpansionKitApi.GetExpandedMenu(ExpandedMenu.WorldDetailsMenu).AddSimpleButton("Local Favorite", ShowFavMenu);

            myPageWorldInfo = GameObject.Find("UserInterface/MenuContent/Screens/WorldInfo").GetComponentInChildren<PageWorldInfo>();
            myPageWorldInfo.gameObject.AddComponent<EnableDisableListener>().OnEnabled += () =>
            {
                MelonCoroutines.Start(EnforceNewInstanceButtonEnabled());
            };
        }

        private IEnumerator EnforceNewInstanceButtonEnabled()
        {
            var endTime = Time.time + 5f;
            while (Time.time < endTime && myPageWorldInfo.gameObject.activeSelf)
            {
                yield return null;
                myPageWorldInfo.transform.Find("WorldButtons/NewButton").GetComponent<Button>().interactable = true;
            }
        } 

        private void ShowFavMenu()
        {
            var availableListsMenu = ExpansionKitApi.CreateCustomFullMenuPopup(LayoutDescription.WideSlimList);
            var currentWorld = myPageWorldInfo.prop_ApiWorld_0;

            var storedCategories = GetCategoriesInSortedOrder();

            if (storedCategories.Count == 0)
                availableListsMenu.AddLabel("Create some categories first before favoriting worlds!");
            
            availableListsMenu.AddSimpleButton("Close", () => availableListsMenu.Hide());

            foreach (var storedCategory in storedCategories)
            {
                if (storedCategory.CategoryName == SearchCategoryName)
                    continue;

                availableListsMenu.AddSimpleButton(
                    $"{(!Favorites.IsFavorite(currentWorld.id, storedCategory.CategoryName) ? "Favorite to" : "Unfavorite from")} {storedCategory.CategoryName}", 
                    self =>
                    {
                        if (Favorites.IsFavorite(currentWorld.id, storedCategory.CategoryName))
                            Favorites.DeleteFavorite(currentWorld.id, storedCategory.CategoryName);
                        else
                            Favorites.AddFavorite(currentWorld.id, storedCategory.CategoryName);

                        self.SetText($"{(!Favorites.IsFavorite(currentWorld.id, storedCategory.CategoryName) ? "Favorite to" : "Unfavorite from")} {storedCategory.CategoryName}");
                        
                        if (FavCatSettings.HidePopupAfterFav.Value) availableListsMenu.Hide();
                    });
            }
            
            availableListsMenu.Show();
        }

        private static Transform GetListsParent()
        {
            var foundWorldsPage = GameObject.Find("UserInterface/MenuContent/Screens/Worlds");
            if (foundWorldsPage == null)
                throw new ApplicationException("No world page, can't initialize extended favorites");

            var randomList = foundWorldsPage.GetComponentInChildren<UiWorldList>(true);
            return randomList.transform.parent;
        }

        private string myLastRequestedWorld = "";
        protected override void OnPickerSelected(IPickerElement picker)
        {
            if (picker.Id == myLastRequestedWorld) 
                return;
            
            PlaySound();

            myLastRequestedWorld = picker.Id;
            var world = new ApiWorld {id = picker.Id};
            world.Fetch(new Action<ApiContainer>(_ =>
            {
                myLastRequestedWorld = "";
                if (listsParent.gameObject.activeInHierarchy)
                    ScanningReflectionCache.DisplayWorldInfoPage(world, null, false, null);
            }), new Action<ApiContainer>(c =>
            {
                myLastRequestedWorld = "";
                if (MelonDebug.IsEnabled())
                    MelonDebug.Msg("API request errored with " + c.Code + " - " + c.Error);
                if (c.Code == 404 && listsParent.gameObject.activeInHierarchy)
                {
                    FavCatMod.Database.CompletelyDeleteWorld(picker.Id);
                    var menu = ExpansionKitApi.CreateCustomFullMenuPopup(LayoutDescription.WideSlimList);
                    menu.AddSpacer();
                    menu.AddSpacer();
                    menu.AddLabel("This world is not available anymore (deleted)");
                    menu.AddLabel("It has been removed from all favorite lists");
                    menu.AddSpacer();
                    menu.AddSpacer();
                    menu.AddSpacer();
                    menu.AddSimpleButton("Close", menu.Hide);
                    menu.Show();
                }
            }));
        }

        protected override void SortModelList(string sortCriteria, string category, List<(StoredFavorite?, StoredWorld)> avatars)
        {
            var inverted = sortCriteria.Length > 0 && sortCriteria[0] == '!';
            Comparison<(StoredFavorite? Fav, StoredWorld Model)> comparison;
            switch (sortCriteria)
            {
                case "name":
                case "!name":
                default:
                    comparison = (a, b) => string.Compare(a.Model.Name, b.Model.Name, StringComparison.InvariantCultureIgnoreCase) * (inverted ? -1 : 1); 
                    break;
                case "updated":
                case "!updated":
                    comparison = (a, b) => a.Model.UpdatedAt.CompareTo(b.Model.UpdatedAt) * (inverted ? -1 : 1);
                    break;
                case "created":
                case "!created":
                    comparison = (a, b) => a.Model.CreatedAt.CompareTo(b.Model.CreatedAt) * (inverted ? -1 : 1);
                    break;
                case "added":
                case "!added":
                    comparison = (a, b) => (a.Fav?.AddedOn ?? DateTime.MinValue).CompareTo(b.Fav?.AddedOn ?? DateTime.MinValue) * (inverted ? -1 : 1);
                    break;
            }
            avatars.Sort(comparison);
        }

        protected override IPickerElement WrapModel(StoredFavorite? favorite, StoredWorld model) => new DbWorldAdapter(model, favorite);

        protected override void SearchButtonClicked()
        {
            BuiltinUiUtils.ShowInputPopup("Local Search (World)", "", InputField.InputType.Standard, false,
                "Search!", (s, list, arg3) =>
                {
                    SetSearchListHeaderAndScrollToIt("Search running...");
                    LastSearchRequest = s;
                    FavCatMod.Database.RunBackgroundWorldSearch(s, AcceptSearchResult);
                });
        }
    }
}