using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.RoomEditor
{
    /// <summary>Liste des salles et éditeur visuel : propriétés, entrées, palette du biome et grille à peindre.</summary>
    public sealed class RoomsTab : AssetListTab<RoomData>
    {
        const string AllBiomes = "Tous les biomes";
        const string AllTypes = "Tous les types";

        static readonly Dictionary<RoomType, Color> TypeColors = new()
        {
            { RoomType.Start, new Color(0.45f, 0.8f, 0.45f) },
            { RoomType.Combat, new Color(0.85f, 0.55f, 0.45f) },
            { RoomType.Treasure, new Color(0.95f, 0.8f, 0.3f) },
            { RoomType.Shop, new Color(0.55f, 0.7f, 0.95f) },
            { RoomType.Boss, new Color(0.85f, 0.25f, 0.25f) }
        };

        DropdownField biomeFilter;
        DropdownField typeFilter;
        RoomPalette palette;
        RoomCanvas canvas;
        SerializedObject room;
        RoomData draft;
        SliderInt widthSlider;
        SliderInt heightSlider;
        readonly Dictionary<Doors, Toggle> doorToggles = new();
        double lastNotification;
        bool closingHooked;

        public RoomsTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouvelle salle";
        protected override string Noun => "salle";
        protected override bool FeminineNoun => true;
        protected override float ListWidth => 220;

        protected override Color GetColor(RoomData asset) => asset.Biome != null ? asset.Biome.Color : Color.gray;
        protected override Sprite GetPreview(RoomData asset) => asset.Biome != null ? asset.Biome.GetTileSprite(TileType.Floor, 0, 0) : null;

        protected override IEnumerable<VisualElement> GetBadges(RoomData asset)
        {
            yield return ForgeUI.Badge(asset.RoomType.ToString(), TypeColors[asset.RoomType], "Type de salle.");
            yield return ForgeUI.Badge(asset.Biome != null ? asset.Biome.DisplayName : "Sans biome",
                asset.Biome != null ? asset.Biome.Color : Color.gray, "Biome de la salle.");
            yield return ForgeUI.Badge($"{asset.Width}×{asset.Height}", new Color(0.7f, 0.7f, 0.7f), "Taille en cases.");
            yield return ForgeUI.Badge($"Difficulté {asset.Difficulty}", new Color(0.85f, 0.6f, 0.6f), "Difficulté ressentie.");
        }

        protected override VisualElement BuildFilters()
        {
            biomeFilter = new DropdownField { tooltip = "N'affiche que les salles de ce biome. Une nouvelle salle prend ce biome." };
            biomeFilter.RegisterCallback<PointerDownEvent>(_ => RefreshBiomeChoices(), TrickleDown.TrickleDown);
            RefreshBiomeChoices();
            biomeFilter.SetValueWithoutNotify(AllBiomes);
            biomeFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            typeFilter = new DropdownField(new List<string> { AllTypes }.Concat(Enum.GetNames(typeof(RoomType))).ToList(), 0)
                { tooltip = "N'affiche que les salles de ce type." };
            typeFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            var filters = new VisualElement();
            filters.Add(biomeFilter);
            filters.Add(typeFilter);
            return filters;
        }

        void RefreshBiomeChoices()
        {
            biomeFilter.choices = new List<string> { AllBiomes }.Concat(AssetFactory.LoadAll<BiomeData>().Select(b => b.DisplayName)).ToList();
        }

        protected override bool PassesFilters(RoomData asset) =>
            (biomeFilter.value == AllBiomes || (asset.Biome != null && asset.Biome.DisplayName == biomeFilter.value)) &&
            (typeFilter.value == AllTypes || typeFilter.value == asset.RoomType.ToString());

        List<BiomeData> ValidBiomes() => AssetFactory.LoadAll<BiomeData>().Where(ValidationCache.IsValid).ToList();

        protected override bool CanCreate(out string reason)
        {
            reason = "Une salle appartient toujours à un biome, et aucun preset de biome valide n'existe.\n\n" +
                     "Créez-en un (ou corrigez-en un) dans Forge Studio > Biomes.";
            return ValidBiomes().Count > 0;
        }

        protected override void InitializeNew(SerializedObject serialized)
        {
            var biomes = ValidBiomes();
            var biome = biomes.FirstOrDefault(b => b.DisplayName == biomeFilter.value) ?? biomes.First();
            serialized.FindProperty("biome").objectReferenceValue = biome;
            serialized.FindProperty("roomType").enumValueIndex = (int)RoomType.Combat;
            var size = RoomEditing.DefaultSize(Limits);
            serialized.FindProperty("width").intValue = size.x;
            serialized.FindProperty("height").intValue = size.y;
            serialized.FindProperty("doors").intValue = (int)(Doors.West | Doors.East);
            RoomEditing.Reset(serialized);
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, RoomData asset)
        {
            room = serializedObject;
            draft = asset;
            doorToggles.Clear();

            var columns = new VisualElement();
            columns.AddToClassList("forge-room-layout");
            container.Add(columns);

            var properties = new VisualElement();
            properties.AddToClassList("forge-room-properties");
            columns.Add(properties);

            var identity = ForgeUI.Section("Salle", properties);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom de la salle. Le fichier sera nommé RM_<Nom> automatiquement."));
            var biomePicker = new AssetPicker<BiomeData>("Biome", serializedObject.FindProperty("biome"),
                "Biome de la salle : il fournit les visuels et la palette (ennemis, décors, coffres).", allowNone: false);
            identity.Add(biomePicker);
            identity.Add(ForgeUI.Button("Retirer ce qui n'appartient pas au biome",
                "Après un changement de biome : retire les ennemis, coffres et décors que le nouveau biome ne propose pas.", () =>
                {
                    var removed = RoomEditing.RemoveForeignContent(room, draft.Biome);
                    room.ApplyModifiedProperties();
                    ForgeLists.Notify(removed == 0 ? "Tout appartient déjà au biome." : $"{removed} élément(s) retiré(s).");
                }));
            identity.Add(ForgeUI.Enum<RoomType>("Type", "roomType",
                "Départ : apparition du joueur. Combat : au moins un ennemi. Trésor : au moins un coffre. Boss : exactement un boss."));
            identity.Add(ForgeUI.Slider("Difficulté", "difficulty", Limits.roomDifficulty, "Difficulté ressentie, de 1 (facile) à 5 (très dur)."));

            // Soin au feu de camp : la règle de l'étage, sauf si la salle a la sienne.
            var healOverride = ForgeUI.Toggle("Soin propre à la salle", "overrideCampfireHeal",
                "Cochée : le feu de camp de cette salle suit le réglage ci-dessous. Décochée : il suit la règle de l'étage (outil Étages).");
            var heal = ForgeUI.Slider("Soin au feu de camp (%)", "campfireHealPercent", new IntRange(0, 100),
                "PV rendus en se reposant au feu de camp de cette salle, en % des PV max (0 = aucun soin).");
            heal.SetEnabled(draft.OverridesCampfireHeal);
            healOverride.RegisterValueChangedCallback(e => heal.SetEnabled(e.newValue));
            identity.Add(healOverride);
            identity.Add(heal);


            var shape = ForgeUI.Section("Dimensions et entrées", properties);
            widthSlider = new SliderInt("Largeur", Limits.roomWidth.min, Limits.roomWidth.max)
                { showInputField = true, value = asset.Width, tooltip = $"Largeur en cases ({Limits.roomWidth}). Le contenu existant est conservé." };
            heightSlider = new SliderInt("Hauteur", Limits.roomHeight.min, Limits.roomHeight.max)
                { showInputField = true, value = asset.Height, tooltip = $"Hauteur en cases ({Limits.roomHeight}). Le contenu existant est conservé." };
            widthSlider.RegisterValueChangedCallback(evt => Resize(evt.newValue, draft.Height));
            heightSlider.RegisterValueChangedCallback(evt => Resize(draft.Width, evt.newValue));
            shape.Add(widthSlider);
            shape.Add(heightSlider);

            shape.Add(ForgeUI.Label("Entrées", "forge-section-title"));
            var doorGrid = new VisualElement();
            doorGrid.AddToClassList("forge-door-picker");
            doorGrid.Add(DoorToggle(Doors.North, "Nord"));
            doorGrid.Add(ForgeLists.Row(DoorToggle(Doors.West, "Ouest"), DoorToggle(Doors.East, "Est")));
            doorGrid.Add(DoorToggle(Doors.South, "Sud"));
            shape.Add(doorGrid);

            container.Add(ForgeLists.Array(serializedObject, "tags", "Tags", "Mots-clés libres pour retrouver et filtrer les salles.", 6,
                (element, _) =>
                {
                    var tag = new TextField { tooltip = "Mot-clé (ex : piège, embuscade, trésor caché)." };
                    tag.BindProperty(element);
                    return tag;
                },
                element =>
                {
                    element.stringValue = string.Empty;
                    return true;
                }));

            var test = ForgeUI.Button("▶  Tester cette salle", "Lance le jeu dans cette salle seule (version enregistrée) : ennemis, coffres, décors.", () =>
            {
                if (ConfirmPlaySavedVersion())
                    TheDungeon.Editor.Play.GameLauncher.PlayRoom(Original);
            }, "forge-play-button");
            properties.Add(test);

            var tools = ForgeUI.Section("Actions rapides", properties);
            tools.Add(ForgeUI.Button("Murs sur le contour", "Remet un mur sur tout le contour, entrées comprises restées ouvertes.", () => Apply(() => RoomEditing.WallBorder(room))));
            tools.Add(ForgeUI.Button("Tout réinitialiser", "Sol partout, murs sur le contour, aucun élément posé.", () =>
            {
                if (ForgeDialogs.Confirm("Réinitialiser la salle", "Effacer toute la salle ? (annulable avec Ctrl+Z)", "Réinitialiser", "Annuler"))
                    Apply(() => RoomEditing.Reset(room));
            }));
            var zoom = new SliderInt("Zoom", 16, 48) { value = 32, showInputField = false, tooltip = "Taille des cases à l'écran." };
            tools.Add(zoom);
            var unreachable = new Toggle("Montrer les zones inaccessibles") { value = true, tooltip = "Teinte en rouge le sol qu'on ne peut pas atteindre depuis les entrées." };
            tools.Add(unreachable);

            var editor = new VisualElement();
            editor.AddToClassList("forge-room-editor");
            columns.Add(editor);

            palette = new RoomPalette();
            canvas = new RoomCanvas(serializedObject, asset, () => palette.Selected, ApplyResult);
            var paletteScroll = new ScrollView();
            paletteScroll.AddToClassList("forge-palette-scroll");
            paletteScroll.Add(palette);
            editor.Add(paletteScroll);
            var canvasScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            canvasScroll.AddToClassList("forge-canvas-scroll");
            canvasScroll.Add(canvas);
            editor.Add(canvasScroll);

            zoom.RegisterValueChangedCallback(evt => canvas.SetZoom(evt.newValue));
            unreachable.RegisterValueChangedCallback(evt =>
            {
                canvas.ShowUnreachable = evt.newValue;
                canvas.Redraw();
            });

            palette.SetBiome(asset.Biome);
            biomePicker.TrackPropertyValue(serializedObject.FindProperty("biome"), _ => palette.SetBiome(draft.Biome));
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            if (!closingHooked)
            {
                closingHooked = true;
                Window.Closing += () => Undo.undoRedoPerformed -= OnUndoRedo;
            }
        }

        Toggle DoorToggle(Doors side, string label)
        {
            var toggle = new Toggle(label) { value = draft.HasDoor(side), tooltip = $"Entrée au {label.ToLowerInvariant()} : ouvre une porte au milieu de ce côté." };
            toggle.AddToClassList("forge-door-toggle");
            toggle.RegisterValueChangedCallback(evt => Apply(() => RoomEditing.SetDoor(room, side, evt.newValue)));
            doorToggles[side] = toggle;
            return toggle;
        }

        void Resize(int width, int height)
        {
            if (width == draft.Width && height == draft.Height)
                return;
            Apply(() => RoomEditing.Resize(room, width, height));
        }

        void Apply(Action operation)
        {
            room.Update();
            operation();
            room.ApplyModifiedProperties();
            canvas.Redraw();
        }

        /// <summary>Résultat d'un coup de pinceau : enregistre, ou affiche pourquoi c'est refusé.</summary>
        string ApplyResult(string error)
        {
            if (error != null)
            {
                room.Update();
                // Évite d'afficher le même message à chaque case survolée pendant un glisser.
                if (EditorApplication.timeSinceStartup - lastNotification > 1.5)
                {
                    ForgeLists.Notify(error);
                    lastNotification = EditorApplication.timeSinceStartup;
                }
                return error;
            }
            room.ApplyModifiedProperties();
            canvas.Redraw();
            return null;
        }

        void OnUndoRedo()
        {
            if (room == null || draft == null)
                return;
            room.Update();
            SyncControls();
            canvas.Redraw();
        }

        protected override void OnRefresh(RoomData asset)
        {
            SyncControls();
            canvas.Redraw();
        }

        void SyncControls()
        {
            widthSlider.SetValueWithoutNotify(draft.Width);
            heightSlider.SetValueWithoutNotify(draft.Height);
            foreach (var (side, toggle) in doorToggles)
                toggle.SetValueWithoutNotify(draft.HasDoor(side));
        }
    }
}
