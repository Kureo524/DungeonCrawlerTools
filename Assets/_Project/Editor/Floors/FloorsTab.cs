using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Floors
{
    /// <summary>
    /// Plans des étages (GDD : 3 étages de 10 salles). Les salles sont placées sur une grille, à la main ou par le générateur ;
    /// le plan montre en direct les portes reliées, les portes cassées, l'accessibilité et le boss.
    /// </summary>
    public sealed class FloorsTab : AssetListTab<FloorData>
    {
        SerializedObject floor;
        FloorData draft;
        FloorCanvas canvas;
        VisualElement palette;
        VisualElement stats;
        Label entranceLabel;
        Button entranceButton;
        Button removeButton;
        Button openButton;
        IntegerField seedField;
        RoomData placing;

        public FloorsTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Étage";
        protected override string Noun => "étage";
        protected override float ListWidth => 200;

        protected override string GetLabel(FloorData asset) => $"Étage {asset.FloorNumber}";
        protected override Color GetColor(FloorData asset) => ChartColors.Series(Mathf.Clamp(asset.FloorNumber - 1, 0, ChartColors.SeriesCount - 1));

        protected override IEnumerable<VisualElement> GetBadges(FloorData asset)
        {
            var map = FloorGraph.Map(asset.Rooms);
            yield return ForgeUI.Badge($"{map.Count}/{Limits.roomsPerFloor} salles", new Color(0.7f, 0.7f, 0.7f), "Salles placées / prévues par le GDD.");
            var bosses = FloorGraph.BossCount(map.Values);
            yield return ForgeUI.Badge($"{bosses} boss", bosses == 1 ? ChartColors.Good : ChartColors.Critical, "Il faut exactement un boss par étage.");
        }

        IEnumerable<int> FreeNumbers()
        {
            var used = AssetFactory.LoadAll<FloorData>().Select(f => f.FloorNumber).ToHashSet();
            return Enumerable.Range(1, Limits.floorCount).Where(n => !used.Contains(n));
        }

        protected override bool CanCreate(out string reason)
        {
            reason = $"Les {Limits.floorCount} étages prévus par le GDD existent déjà.";
            return FreeNumbers().Any();
        }

        protected override void InitializeNew(SerializedObject serialized)
        {
            serialized.FindProperty("floorNumber").intValue = FreeNumbers().First();
        }

        /// <summary>
        /// Salles utilisables sur cet étage : valides, d'un biome autorisé à cet étage, absentes des autres étages,
        /// et du biome de l'étage dès qu'une salle y est placée.
        /// </summary>
        List<RoomData> AvailableRooms()
        {
            var otherFloors = AssetFactory.LoadAll<FloorData>().Where(f => f != Original);
            var elsewhere = otherFloors.SelectMany(f => f.Rooms).Select(s => s.room).Where(r => r != null).ToHashSet();
            var floorBiome = FloorGraph.BiomeOf(FloorGraph.Map(draft.Rooms).Values);
            return AssetFactory.LoadAll<RoomData>()
                .Where(r => ValidationCache.IsValid(r) && !elsewhere.Contains(r) && r.Biome != null &&
                            draft.FloorNumber >= r.Biome.MinFloor && draft.FloorNumber <= r.Biome.MaxFloor &&
                            (floorBiome == null || r.Biome == floorBiome))
                .ToList();
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, FloorData asset)
        {
            floor = serializedObject;
            draft = asset;
            placing = null;

            stats = new VisualElement();
            stats.AddToClassList("forge-card-grid");
            container.Add(stats);

            BuildSettings(container);
            BuildGenerator(container);
            BuildEditor(container, serializedObject, asset);
        }

        /// <summary>Numéro de l'étage, entrée et soin au feu de camp.</summary>
        void BuildSettings(VisualElement container)
        {
            var settings = ForgeUI.Section("Étage", container);
            settings.Add(ForgeUI.Slider("Numéro", "floorNumber", new IntRange(1, Limits.floorCount),
                "Profondeur de l'étage. Détermine les biomes et les salles autorisés."));
            entranceLabel = ForgeUI.Label(string.Empty);
            settings.Add(entranceLabel);
            settings.Add(ForgeUI.Slider("Soin au feu de camp (%)", "campfireHealPercent", new IntRange(0, 100),
                "PV rendus en se reposant à un feu de camp, en % des PV max (0 = aucun soin). Règle de tout l'étage ; une salle peut avoir la sienne (outil Salles)."));
        }

        /// <summary>Génération automatique du plan à partir d'une graine.</summary>
        void BuildGenerator(VisualElement container)
        {
            var generator = ForgeUI.Section("Génération automatique", container);
            generator.Add(ForgeUI.Label(
                "Assemble un plan avec les salles disponibles : départ au centre, portes toutes reliées, boss en fond d'étage. " +
                "Le résultat reste modifiable à la main.", "forge-hint"));
            seedField = new IntegerField("Graine") { value = Environment.TickCount % 10000, tooltip = "Même graine = même plan. La graine retenue s'affiche ici après la génération." };
            generator.Add(ForgeLists.Row(seedField,
                ForgeUI.Button("Nouvelle graine", "Tire une graine au hasard.", () => seedField.value = UnityEngine.Random.Range(0, 10000)),
                ForgeUI.Button("Générer le plan", "Remplace le plan actuel par un autre plan, dans le biome de l'étage (annulable avec Ctrl+Z). Si la graine donne le plan actuel, les suivantes sont essayées.", Generate, "forge-button--primary"),
                ForgeUI.Button("Vider le plan", "Retire toutes les salles du plan.", () => Edit(() => FloorEditing.Clear(floor)), "forge-button--danger")));
        }

        /// <summary>Bouton de test, puis salles disponibles et plan.</summary>
        void BuildEditor(VisualElement container, SerializedObject serializedObject, FloorData asset)
        {
            var test = ForgeUI.Button("▶  Tester cet étage", "Lance le jeu directement sur cet étage (version enregistrée).", () =>
            {
                if (ConfirmPlaySavedVersion())
                    TheDungeon.Editor.Play.GameLauncher.PlayFloor(Original.FloorNumber);
            }, "forge-play-button");
            test.style.alignSelf = Align.FlexStart;
            container.Add(test);

            var editor = new VisualElement();
            editor.AddToClassList("forge-room-editor");
            container.Add(editor);
            editor.Add(BuildRoomSide());
            editor.Add(BuildPlan(serializedObject, asset));
        }

        /// <summary>Salles disponibles à placer et actions sur la salle sélectionnée.</summary>
        VisualElement BuildRoomSide()
        {
            var left = new VisualElement();
            left.AddToClassList("forge-floor-side");
            left.Add(ForgeUI.Label(ForgeText.WindowRich("Salles disponibles"), "forge-palette__title"));
            left.Add(ForgeUI.Label("Cliquez une salle puis une case du plan. Les cases vertes acceptent ses portes.", "forge-hint"));
            var paletteScroll = new ScrollView();
            paletteScroll.AddToClassList("forge-palette-scroll");
            palette = new VisualElement();
            paletteScroll.Add(palette);
            left.Add(paletteScroll);

            left.Add(ForgeUI.Label(ForgeText.WindowRich("Salle sélectionnée"), "forge-palette__title"));
            entranceButton = ForgeUI.Button("Définir comme entrée", "Le joueur arrive sur l'étage par cette salle.", () =>
            {
                if (canvas.SelectedRoom != null)
                    Edit(() => FloorEditing.SetEntrance(floor, canvas.SelectedRoom));
            });
            removeButton = ForgeUI.Button("Retirer du plan", "Retire la salle (elle redevient disponible).", () =>
            {
                var room = canvas.SelectedRoom;
                if (room != null && FloorGraph.TryFindCell(FloorGraph.Map(draft.Rooms), room, out var cell))
                    Edit(() => FloorEditing.RemoveAt(floor, cell));
            });
            openButton = ForgeUI.Button("Ouvrir dans Salles", "Ouvre la salle dans l'éditeur de salles.", () => Window.Reveal(canvas.SelectedRoom));
            left.Add(entranceButton);
            left.Add(removeButton);
            left.Add(openButton);
            return left;
        }

        /// <summary>Plan de l'étage, zoom et légende des portes.</summary>
        VisualElement BuildPlan(SerializedObject serializedObject, FloorData asset)
        {
            var right = new VisualElement();
            right.style.flexGrow = 1;
            var zoom = new SliderInt("Zoom", 60, 140) { value = 96, tooltip = "Taille des salles à l'écran." };
            right.Add(zoom);
            canvas = new FloorCanvas(serializedObject, asset, Limits.floorGridSize, () => placing, Edit, room => Window.Reveal(room), () =>
            {
                placing = null;
                RefreshPalette();
            });
            canvas.SelectionChanged += RefreshSelectionButtons;
            zoom.RegisterValueChangedCallback(evt => canvas.SetZoom(evt.newValue));
            var canvasScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            canvasScroll.AddToClassList("forge-canvas-scroll");
            canvasScroll.Add(canvas);
            right.Add(canvasScroll);
            right.Add(ChartLegend.Build(new[]
            {
                ("Portes reliées", ChartColors.Good),
                ("Porte dans le vide / incompatible", ChartColors.Critical),
                ("Inaccessible depuis l'entrée", ChartColors.Warning)
            }));
            return right;
        }

        void Edit(Action operation)
        {
            floor.Update();
            operation();
            floor.ApplyModifiedProperties();
            canvas.Redraw();
            RefreshPalette();
            RefreshSelectionButtons();
        }

        void Generate()
        {
            var current = FloorGraph.Map(draft.Rooms);
            var pool = AvailableRooms().Concat(current.Values).Distinct().ToList();
            var result = FloorGenerator.GenerateDifferent(pool, Limits.roomsPerFloor, Limits.floorGridSize, seedField.value, current, out var usedSeed, out var changed);
            if (!result.Success)
            {
                ForgeDialogs.Inform("Génération impossible", result.Error);
                return;
            }
            if (!changed)
            {
                ForgeLists.Notify("Ces salles ne permettent pas d'autre plan : ajoutez des salles avec d'autres portes pour varier.");
                return;
            }
            seedField.SetValueWithoutNotify(usedSeed);
            Edit(() => FloorEditing.Replace(floor, result.Layout, result.Entrance));
            ForgeLists.Notify($"Plan généré : {result.Layout.Count} salles (graine {usedSeed}).");
        }

        protected override void OnRefresh(FloorData asset)
        {
            canvas.Redraw();
            RefreshPalette();
            RefreshStats();
            RefreshSelectionButtons();
            entranceLabel.text = asset.Entrance != null
                ? $"Entrée : <b>{asset.Entrance.DisplayName}</b>"
                : "Entrée : <color=#d03b3b>aucune</color> (sélectionnez une salle puis « Définir comme entrée »)";
        }

        void RefreshSelectionButtons()
        {
            var hasSelection = canvas.SelectedRoom != null;
            entranceButton.SetEnabled(hasSelection && canvas.SelectedRoom != draft.Entrance);
            removeButton.SetEnabled(hasSelection);
            openButton.SetEnabled(hasSelection);
        }

        void RefreshPalette()
        {
            palette.Clear();
            var placed = FloorGraph.Map(draft.Rooms).Values.ToHashSet();
            var rooms = AvailableRooms().OrderBy(r => r.RoomType).ThenBy(r => r.DisplayName).ToList();
            if (placing != null && !rooms.Contains(placing))
                placing = null;
            var floorBiome = FloorGraph.BiomeOf(placed);
            if (floorBiome != null)
                palette.Add(ForgeUI.Label($"Biome de l'étage : {ForgeText.Name(floorBiome.DisplayName)}. Videz le plan pour en changer.", "forge-hint"));
            if (rooms.Count == 0)
            {
                palette.Add(ForgeUI.Label("Aucune salle valide disponible pour cet étage. Créez des salles dans Forge Studio > Salles.", "forge-hint"));
                return;
            }
            foreach (var room in rooms)
            {
                var card = new VisualElement { tooltip = $"{room.DisplayName} ({room.RoomType}, {room.Biome.DisplayName})\nPortes : {DoorList(room)}" };
                card.AddToClassList("forge-floor-room");
                card.EnableInClassList("forge-floor-room--placed", placed.Contains(room));
                card.EnableInClassList("forge-floor-room--selected", room == placing);
                var mini = new VisualElement { pickingMode = PickingMode.Ignore };
                mini.AddToClassList("forge-floor-room__mini");
                mini.generateVisualContent += ctx => RoomMinimap.Draw(ctx.painter2D, new Rect(0, 0, 44, 32), room);
                card.Add(mini);
                var texts = new VisualElement { pickingMode = PickingMode.Ignore };
                texts.Add(new Label(room.DisplayName) { pickingMode = PickingMode.Ignore });
                var detail = new Label($"{room.RoomType} · {DoorArrows(room)}{(placed.Contains(room) ? " · placée" : string.Empty)}") { pickingMode = PickingMode.Ignore };
                detail.AddToClassList("forge-floor-room__detail");
                texts.Add(detail);
                card.Add(texts);
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    placing = placing == room ? null : room;
                    RefreshPalette();
                    canvas.Redraw();
                });
                palette.Add(card);
            }
        }

        void RefreshStats()
        {
            stats.Clear();
            var map = FloorGraph.Map(draft.Rooms);
            var broken = FloorGraph.AllDoors(map).Count(d => d.State != DoorState.Connected);
            var bosses = FloorGraph.BossCount(map.Values);
            var reachable = draft.Entrance != null && FloorGraph.TryFindCell(map, draft.Entrance, out var start)
                ? FloorGraph.Reachable(map, start).Count
                : 0;
            Stat("Salles", $"{map.Count} / {Limits.roomsPerFloor}", map.Count == Limits.roomsPerFloor, "Nombre de salles prévu par le GDD.");
            Stat("Boss", $"{bosses} / 1", bosses == 1, "Exactement un boss par étage (GDD).");
            Stat("Portes non reliées", broken.ToString(), broken == 0, "Portes qui mènent dans le vide ou vers une salle sans porte en face.");
            Stat("Accessibles", map.Count == 0 ? "—" : $"{reachable} / {map.Count}", map.Count > 0 && reachable == map.Count,
                "Salles atteignables depuis l'entrée (GDD).");
        }

        void Stat(string title, string value, bool ok, string tooltip)
        {
            var tile = new VisualElement { tooltip = tooltip };
            tile.AddToClassList("forge-card");
            tile.AddToClassList("forge-stat");
            tile.AddToClassList(ok ? "forge-stat--good" : "forge-stat--critical");
            tile.Add(ForgeUI.Label($"{(ok ? "✔" : "✖")}  {title}", "forge-stat__title"));
            tile.Add(ForgeUI.Label(value, "forge-stat__value"));
            stats.Add(tile);
        }

        static string DoorList(RoomData room) =>
            string.Join(", ", FloorData.Sides.Where(room.HasDoor).Select(FloorGraph.SideName).DefaultIfEmpty("aucune"));

        static string DoorArrows(RoomData room) => string.Concat(FloorData.Sides.Where(room.HasDoor).Select(side => side switch
        {
            Doors.North => "↑",
            Doors.East => "→",
            Doors.South => "↓",
            _ => "←"
        }));
    }
}
