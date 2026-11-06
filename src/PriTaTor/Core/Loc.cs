// Interface languages. The program's own text is English and the English text is the key:
// Loc.T("Save") returns the Spanish or Portuguese words when one of those is chosen, and "Save" otherwise.
// A text with no entry is shown as written, so a missing translation is never an error.
// Loc.Apply walks a window and swaps the text of every label, button, menu, tab and column.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace PriTaTor.Core
{
    public sealed class Language
    {
        public readonly string Code, Name;
        public Language(string code, string name) { Code = code; Name = name; }
    }

    public static partial class Loc
    {
        public static readonly Language[] All =
        {
            new Language("en", "English"),
            new Language("es", "Español"),
            new Language("pt", "Português")
        };

        public static string Code { get; private set; } = "en";
        public static event Action Changed;

        static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PriTaTor", "language.txt");

        /// <summary>Reads the saved choice. With none saved the language stays English, the default.</summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                string c = File.ReadAllText(SettingsPath).Trim().ToLowerInvariant();
                foreach (Language l in All) if (l.Code == c) Code = c;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Set(string code)
        {
            if (code == Code) return;
            Code = code;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, code);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Changed?.Invoke();
        }

        /// <summary>The text in the chosen language.</summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english) || Code == "en") return english;
            int i = Code == "es" ? 0 : 1;
            return Table.TryGetValue(english, out string[] t) && !string.IsNullOrEmpty(t[i]) ? t[i] : english;
        }

        /// <summary>A text with values in it: the English form is the key, such as "{0} maps".</summary>
        public static string F(string english, params object[] args) => string.Format(T(english), args);

        /// <summary>Several lines, each translated by itself.</summary>
        public static string Lines(string english)
        {
            if (Code == "en") return english;
            string[] p = english.Split('\n');
            for (int i = 0; i < p.Length; i++) p[i] = T(p[i]);
            return string.Join("\n", p);
        }

        // ----- walking a window -----

        // What each piece of text said in English, so a second change of language starts from English and not from Spanish.
        static readonly ConditionalWeakTable<object, Dictionary<string, string[]>> seen = new ConditionalWeakTable<object, Dictionary<string, string[]>>();

        static string Swap(object owner, string slot, string current)
        {
            if (string.IsNullOrEmpty(current)) return current;
            Dictionary<string, string[]> d = seen.GetOrCreateValue(owner);
            string english = current;
            // [0] is the English, [1] what was put on screen: when the screen still shows that, the English is still right
            if (d.TryGetValue(slot, out string[] was) && was[1] == current) english = was[0];
            string shown = TranslateMarked(english);
            d[slot] = new[] { english, shown };
            return shown;
        }

        /// <summary>Tabs mark unsaved changes with a trailing " *".</summary>
        static string TranslateMarked(string s)
        {
            if (s.EndsWith(" *", StringComparison.Ordinal)) return T(s.Substring(0, s.Length - 2)) + " *";
            return T(s);
        }

        public static void Apply(Control c)
        {
            if (c == null) return;
            if (c is Form || c is TabPage || c is Label || c is ButtonBase || c is GroupBox)
            {
                string t = Swap(c, "t", c.Text);
                if (t != c.Text) c.Text = t;
            }
            if (c is ToolStrip ts) ApplyItems(ts.Items);
            if (c.ContextMenuStrip != null) ApplyItems(c.ContextMenuStrip.Items);
            if (c is ListView lv)
                foreach (ColumnHeader h in lv.Columns) { string t = Swap(h, "t", h.Text); if (t != h.Text) h.Text = t; }
            if (c is DataGridView g)
                foreach (DataGridViewColumn col in g.Columns) { string t = Swap(col, "t", col.HeaderText); if (t != col.HeaderText) col.HeaderText = t; }
            foreach (Control child in c.Controls) Apply(child);
        }

        static void ApplyItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem i in items)
            {
                if (!(i is ToolStripSeparator) && !(i is ToolStripControlHost && !(i is ToolStripComboBox)))
                {
                    string t = Swap(i, "t", i.Text);
                    if (t != i.Text) i.Text = t;
                }
                string tip = Swap(i, "tip", i.ToolTipText);
                if (tip != i.ToolTipText) i.ToolTipText = tip;
                if (i is ToolStripDropDownItem d && d.HasDropDownItems) ApplyItems(d.DropDownItems);
                if (i is ToolStripControlHost h && h.Control != null) Apply(h.Control);
            }
        }

        // ----- the words -----

        static readonly Dictionary<string, string[]> Table = Build();

        static Dictionary<string, string[]> Build()
        {
            var d = new Dictionary<string, string[]>(StringComparer.Ordinal);
            // English | Spanish | Portuguese
            void A(string en, string es, string pt) { d[en] = new[] { es, pt }; }

            // menus
            A("&File", "&Archivo", "&Arquivo");
            A("&Save", "&Guardar", "&Salvar");
            A("&Rebuild settings and terrain pictures", "&Reconstruir ajustes e imágenes del terreno", "&Reconstruir configurações e imagens do terreno");
            A("Use a different &Field folder…", "Usar otra carpeta &Field…", "Usar outra pasta &Field…");
            A("Use textures from a &client folder…", "Usar texturas de una carpeta de &cliente…", "Usar texturas de uma pasta do &cliente…");
            A("Stop using client &folder textures", "Dejar de usar las texturas de la carpeta del cl&iente", "Parar de usar as texturas da pasta do cl&iente");
            A("Open &backups folder", "Abrir carpeta de &copias de seguridad", "Abrir pasta de &backups");
            A("Open &terrain pictures folder", "Abrir carpeta de imágenes del &terreno", "Abrir pasta de imagens do &terreno");
            A("E&xit", "&Salir", "Sai&r");
            A("&Help", "A&yuda", "A&juda");
            A("&Map editor keys", "&Teclas del editor de mapas", "&Teclas do editor de mapas");
            A("&About", "&Acerca de", "&Sobre");
            A("&Languages", "&Idiomas", "&Idiomas");

            // main tabs
            A("Maps", "Mapas", "Mapas");
            A("Monsters", "Monstruos", "Monstros");
            A("NPCs", "NPCs", "NPCs");
            A("Items", "Items", "Itens");
            A("Names", "Nombres", "Nomes");
            A("Text files", "Archivos de texto", "Arquivos de texto");

            // common
            A("Save", "Guardar", "Salvar");
            A("Revert", "Revertir", "Reverter");
            A("Add", "Añadir", "Adicionar");
            A("Delete", "Eliminar", "Excluir");
            A("Cancel", "Cancelar", "Cancelar");
            A("Duplicate", "Duplicar", "Duplicar");
            A("Active", "Activo", "Ativo");
            A("On", "Act.", "Ativ.");
            A("Name", "Nombre", "Nome");
            A("File", "Archivo", "Arquivo");
            A("Code", "Código", "Código");
            A("Level", "Nivel", "Nível");
            A("Price", "Precio", "Preço");
            A("Item", "Item", "Item");
            A("Korean", "Coreano", "Coreano");
            A("Group", "Grupo", "Grupo");
            A("Field", "Campo", "Campo");
            A("Key", "Clave", "Chave");
            A("Value", "Valor", "Valor");
            A("What it does", "Qué hace", "O que faz");
            A("Other", "Otros", "Outros");
            A("Fields", "Campos", "Campos");
            A("Raw text", "Texto sin formato", "Texto bruto");
            A("Shop", "Tienda", "Loja");
            A("Add field:", "Añadir campo:", "Adicionar campo:");
            A("Remove row", "Quitar fila", "Remover linha");
            A("Open name file", "Abrir archivo de nombres", "Abrir arquivo de nomes");
            A("New copy…", "Nueva copia…", "Nova cópia…");
            A("Delete file", "Eliminar archivo", "Excluir arquivo");
            A("Search name or file", "Buscar nombre o archivo", "Buscar nome ou arquivo");
            A("Find:", "Buscar:", "Localizar:");
            A("Next", "Siguiente", "Próximo");
            A("Loading items…", "Cargando items…", "Carregando itens…");

            // map editor
            A("Create a monster list for this map", "Crear una lista de monstruos para este mapa", "Criar uma lista de monstros para este mapa");
            A("Filter maps", "Filtrar mapas", "Filtrar mapas");
            A("Undo", "Deshacer", "Desfazer");
            A("Redo", "Rehacer", "Refazer");
            A("Select", "Seleccionar", "Selecionar");
            A("Add flag", "Añadir bandera", "Adicionar bandeira");
            A("Add NPC", "Añadir NPC", "Adicionar NPC");
            A("Fit", "Ajustar", "Ajustar");
            A("Terrain", "Terreno", "Terreno");
            A("Minimap", "Minimapa", "Minimapa");
            A("Flags", "Banderas", "Bandeiras");
            A("Align minimap", "Alinear minimapa", "Alinhar minimapa");
            A("Save this map's files (Ctrl+S)", "Guardar los archivos de este mapa (Ctrl+S)", "Salvar os arquivos deste mapa (Ctrl+S)");
            A("Undo (Ctrl+Z)", "Deshacer (Ctrl+Z)", "Desfazer (Ctrl+Z)");
            A("Redo (Ctrl+Y)", "Rehacer (Ctrl+Y)", "Refazer (Ctrl+Y)");
            A("Click and drag flags and NPCs. Drag empty ground to box-select.", "Haz clic y arrastra banderas y NPCs. Arrastra el suelo vacío para seleccionar con un recuadro.", "Clique e arraste bandeiras e NPCs. Arraste o chão vazio para selecionar com uma caixa.");
            A("Click on the map to place monster flags", "Haz clic en el mapa para colocar banderas de monstruos", "Clique no mapa para colocar bandeiras de monstros");
            A("Click on the map to place an NPC", "Haz clic en el mapa para colocar un NPC", "Clique no mapa para colocar um NPC");
            A("Delete what's selected (Del)", "Eliminar lo seleccionado (Supr)", "Excluir o que está selecionado (Del)");
            A("Show the whole map (F)", "Mostrar todo el mapa (F)", "Mostrar o mapa inteiro (F)");
            A("Minimap opacity", "Opacidad del minimapa", "Opacidade do minimapa");
            A("Drag the minimap by hand. Flags and NPCs don't move.", "Arrastra el minimapa a mano. Las banderas y los NPCs no se mueven.", "Arraste o minimapa à mão. Bandeiras e NPCs não se movem.");
            A("Shown as", "Mostrado como", "Mostrado como");
            A("NPC file", "Archivo NPC", "Arquivo NPC");
            A("Model", "Modelo", "Modelo");
            A("Position", "Posición", "Posição");
            A("Snap to ground", "Pegar al suelo", "Colar no chão");
            A("Height", "Altura", "Altura");
            A("° (0 = up the map)", "° (0 = hacia arriba del mapa)", "° (0 = para cima no mapa)");
            A("Facing", "Orientación", "Direção");
            A("Open .npc", "Abrir .npc", "Abrir .npc");
            A("Edit shop…", "Editar tienda…", "Editar loja…");
            A("Max alive at once", "Máx. vivos a la vez", "Máx. vivos ao mesmo tempo");
            A("Spawned each time", "Aparecen cada vez", "Surgem a cada vez");
            A("Interval min", "Intervalo mín.", "Intervalo mín.");
            A("Interval max", "Intervalo máx.", "Intervalo máx.");
            A("Monster", "Monstruo", "Monstro");
            A("Rate", "Probabilidad", "Taxa");
            A("Boss", "Jefe", "Chefe");
            A("Guards", "Guardias", "Guardas");
            A("No.", "N.º", "N.º");
            A("Hours", "Horas", "Horas");
            A("Bosses (hours when they appear)", "Jefes (horas en que aparecen)", "Chefes (horas em que aparecem)");
            A("Map", "Mapa", "Mapa");
            A("Same map elsewhere (moved copies)", "Mismo mapa en otro lugar (copias movidas)", "Mesmo mapa em outro lugar (cópias movidas)");
            A("Import flags, NPCs and monsters…", "Importar banderas, NPCs y monstruos…", "Importar bandeiras, NPCs e monstros…");
            A("Shift everything on this map…", "Desplazar todo en este mapa…", "Deslocar tudo neste mapa…");
            A("Choose a different minimap…", "Elegir otro minimapa…", "Escolher outro minimapa…");
            A("Reset minimap position", "Restablecer la posición del minimapa", "Redefinir a posição do minimapa");
            A("Add monster flag here", "Añadir bandera de monstruo aquí", "Adicionar bandeira de monstro aqui");
            A("Add NPC here…", "Añadir NPC aquí…", "Adicionar NPC aqui…");
            A("Turn selected NPC to face here", "Girar el NPC seleccionado hacia aquí", "Virar o NPC selecionado para cá");
            A("Pick a map on the left", "Elige un mapa a la izquierda", "Escolha um mapa à esquerda");
            A("Click to place a monster flag. Esc to stop.", "Haz clic para colocar una bandera de monstruo. Esc para terminar.", "Clique para colocar uma bandeira de monstro. Esc para parar.");
            A("Click to place an NPC. Esc to stop.", "Haz clic para colocar un NPC. Esc para terminar.", "Clique para colocar um NPC. Esc para parar.");
            A("Drag to line the minimap up with the terrain. Esc to finish.", "Arrastra para alinear el minimapa con el terreno. Esc para terminar.", "Arraste para alinhar o minimapa com o terreno. Esc para terminar.");
            A("{0} units", "{0} unidades", "{0} unidades");
            A("Opening {0}…", "Abriendo {0}…", "Abrindo {0}…");
            A("(top)", "(raíz)", "(raiz)");
            A("(no map file)", "(sin archivo de mapa)", "(sem arquivo de mapa)");
            A("{0} of {1} slots", "{0} de {1} espacios", "{0} de {1} espaços");
            A("raw {0}", "bruto {0}", "bruto {0}");
            A("X {0:0}   Z {1:0}{2}      {3} flags, {4} NPCs selected: {5}", "X {0:0}   Z {1:0}{2}      {3} banderas, {4} NPCs; seleccionados: {5}", "X {0:0}   Z {1:0}{2}      {3} bandeiras, {4} NPCs; selecionados: {5}");
            A("   height {0:0}", "   altura {0:0}", "   altura {0:0}");
            A("Copy position ({0:0}, {1:0})", "Copiar posición ({0:0}, {1:0})", "Copiar posição ({0:0}, {1:0})");
            A("Rates add up to {0}. Red names have no monster file.", "Las probabilidades suman {0}. Los nombres en rojo no tienen archivo de monstruo.", "As taxas somam {0}. Nomes em vermelho não têm arquivo de monstro.");
            A("None found.", "No se encontró ninguno.", "Nenhum encontrado.");
            A("{0}: this map is X {1:+0;-0;0}, Z {2:+0;-0;0} from it{3}", "{0}: este mapa está a X {1:+0;-0;0}, Z {2:+0;-0;0} de él{3}", "{0}: este mapa está a X {1:+0;-0;0}, Z {2:+0;-0;0} dele{3}");
            A(" (differs in places)", " (difiere en algunos puntos)", " (difere em alguns pontos)");
            A("File: Field\\{0}", "Archivo: Field\\{0}", "Arquivo: Field\\{0}");
            A("World X {0:0} to {1:0}, Z {2:0} to {3:0}", "X del mundo {0:0} a {1:0}, Z {2:0} a {3:0}", "X do mundo {0:0} a {1:0}, Z {2:0} a {3:0}");
            A("Centre {0:0}, {1:0}. Size {2:0} x {3:0}", "Centro {0:0}, {1:0}. Tamaño {2:0} x {3:0}", "Centro {0:0}, {1:0}. Tamanho {2:0} x {3:0}");
            A("{0:N0} vertices, {1:N0} faces", "{0:N0} vértices, {1:N0} caras", "{0:N0} vértices, {1:N0} faces");
            A("No .smd file for this map in the Field folder.", "No hay archivo .smd para este mapa en la carpeta Field.", "Não há arquivo .smd para este mapa na pasta Field.");
            A("Terrain: textured", "Terreno: con texturas", "Terreno: com texturas");
            A("Terrain: plain. For real textures, see File > Use textures from a client folder", "Terreno: liso. Para texturas reales, usa Archivo > Usar texturas de una carpeta de cliente", "Terreno: liso. Para texturas reais, use Arquivo > Usar texturas de uma pasta do cliente");
            A("Minimap: {0}", "Minimapa: {0}", "Minimapa: {0}");
            A("none, showing terrain only", "ninguno, se muestra solo el terreno", "nenhum, mostrando só o terreno");
            A("Minimap moved by hand: {0:0}, {1:0}", "Minimapa movido a mano: {0:0}, {1:0}", "Minimapa movido à mão: {0:0}, {1:0}");
            A("Minimap stretched to the map's bounds", "Minimapa ajustado a los límites del mapa", "Minimapa ajustado aos limites do mapa");
            A("Spawn files: {0}", "Archivos de aparición: {0}", "Arquivos de surgimento: {0}");
            A("none yet, they're made when you save", "ninguno aún, se crean al guardar", "nenhum ainda, são criados ao salvar");
            A("{0} flags or NPCs are outside this map's bounds (shown with an orange ring). If most are, the map may have moved: use Shift.", "{0} banderas o NPCs están fuera de los límites de este mapa (con un anillo naranja). Si la mayoría lo están, el mapa pudo haberse movido: usa Desplazar.", "{0} bandeiras ou NPCs estão fora dos limites deste mapa (com um anel laranja). Se a maioria estiver, o mapa pode ter se movido: use Deslocar.");
            A("The server program doesn't list this map, so it may not load it, even with spawn files.", "El programa del servidor no incluye este mapa, así que puede que no lo cargue, aunque tenga archivos de aparición.", "O programa do servidor não lista este mapa, então pode não carregá-lo, mesmo com arquivos de surgimento.");
            A("Choose a minimap", "Elegir un minimapa", "Escolher um minimapa");
            A("Minimaps (*.tga;*.bmp)|*.tga;*.bmp", "Minimapas (*.tga;*.bmp)|*.tga;*.bmp", "Minimapas (*.tga;*.bmp)|*.tga;*.bmp");

            // messages and status
            A("PriTaTor Reborn", "PriTaTor Reborn", "PriTaTor Reborn");
            A("Priston Tale server manager and map editor", "Administrador de servidor y editor de mapas de Priston Tale", "Gerenciador de servidor e editor de mapas de Priston Tale");
            A("Priston Tale server manager and map editor.", "Administrador de servidor y editor de mapas de Priston Tale.", "Gerenciador de servidor e editor de mapas de Priston Tale.");
            A("Starting up…", "Iniciando…", "Iniciando…");
            A("Looking for maps…", "Buscando mapas…", "Procurando mapas…");
            A("Reading flags and NPCs…", "Leyendo banderas y NPCs…", "Lendo bandeiras e NPCs…");
            A("Preparing maps: {0}", "Preparando mapas: {0}", "Preparando mapas: {0}");
            A("Looking for moved copies of maps", "Buscando copias movidas de mapas", "Procurando cópias movidas de mapas");
            A("{0}  ({1} of {2})", "{0}  ({1} de {2})", "{0}  ({1} de {2})");
            A("{0} seconds so far. ", "{0} segundos hasta ahora. ", "{0} segundos até agora. ");
            A("Each map is only prepared once. Later starts skip this unless a map file changes.", "Cada mapa se prepara solo una vez. Los siguientes inicios lo omiten salvo que cambie un archivo de mapa.", "Cada mapa é preparado só uma vez. As próximas inicializações pulam isso, a menos que um arquivo de mapa mude.");
            A("Rebuilding server settings…", "Reconstruyendo los ajustes del servidor…", "Reconstruindo as configurações do servidor…");
            A("Reading the server folder…", "Leyendo la carpeta del servidor…", "Lendo a pasta do servidor…");
            A("Reading monsters, NPCs and names…", "Leyendo monstruos, NPCs y nombres…", "Lendo monstros, NPCs e nomes…");
            A("Opening the editor…", "Abriendo el editor…", "Abrindo o editor…");
            A("Couldn't read the server folder:\n\n{0}\n\nDetails are in PriTaTor\\errors.log.", "No se pudo leer la carpeta del servidor:\n\n{0}\n\nLos detalles están en PriTaTor\\errors.log.", "Não foi possível ler a pasta do servidor:\n\n{0}\n\nOs detalhes estão em PriTaTor\\errors.log.");
            A("Couldn't read the server folder.", "No se pudo leer la carpeta del servidor.", "Não foi possível ler a pasta do servidor.");
            A("{0} maps, {1} with spawn data, {2} matched to a moved copy of themselves.", "{0} mapas, {1} con datos de aparición, {2} emparejados con una copia movida de sí mismos.", "{0} mapas, {1} com dados de surgimento, {2} associados a uma cópia movida de si mesmos.");
            A("  Just prepared: {0}.", "  Recién preparados: {0}.", "  Recém-preparados: {0}.");
            A("  This folder can't be saved to, so maps are prepared again at each start.", "  No se puede guardar en esta carpeta, así que los mapas se preparan de nuevo en cada inicio.", "  Não é possível salvar nesta pasta, então os mapas são preparados de novo a cada inicialização.");
            A("  For textured maps, copy the client's Field textures to the server's Field folder, or use File > Use textures from a client folder.", "  Para mapas con texturas, copia las texturas Field del cliente a la carpeta Field del servidor, o usa Archivo > Usar texturas de una carpeta de cliente.", "  Para mapas com texturas, copie as texturas Field do cliente para a pasta Field do servidor, ou use Arquivo > Usar texturas de uma pasta do cliente.");
            A("Spawns on: {0}", "Aparece en: {0}", "Surge em: {0}");
            A("Not in any map's monster list.", "No está en la lista de monstruos de ningún mapa.", "Não está na lista de monstros de nenhum mapa.");
            A("Choose the Field folder holding the map files", "Elige la carpeta Field que contiene los archivos de mapa", "Escolha a pasta Field que contém os arquivos de mapa");
            A("Choose a game client folder. Maps are drawn with the textures in its Field folder.", "Elige la carpeta de un cliente del juego. Los mapas se dibujan con las texturas de su carpeta Field.", "Escolha a pasta de um cliente do jogo. Os mapas são desenhados com as texturas da sua pasta Field.");
            A("Nothing has been backed up yet.", "Aún no se ha hecho ninguna copia de seguridad.", "Ainda não foi feito nenhum backup.");
            A("No terrain pictures have been saved yet.", "Aún no se ha guardado ninguna imagen del terreno.", "Ainda não foi salva nenhuma imagem do terreno.");
            A("No .npc file called {0} in GameServer\\NPC.", "No hay ningún archivo .npc llamado {0} en GameServer\\NPC.", "Não há nenhum arquivo .npc chamado {0} em GameServer\\NPC.");
            A("Map editor keys", "Teclas del editor de mapas", "Teclas do editor de mapas");
            A("About PriTaTor Reborn", "Acerca de PriTaTor Reborn", "Sobre o PriTaTor Reborn");
            A("Left click: select. Ctrl+click: add to selection.", "Clic izquierdo: seleccionar. Ctrl+clic: añadir a la selección.", "Clique esquerdo: selecionar. Ctrl+clique: adicionar à seleção.");
            A("Drag a flag or NPC: move it. Drag empty ground: box select.", "Arrastrar una bandera o NPC: moverlo. Arrastrar suelo vacío: selección con recuadro.", "Arrastar uma bandeira ou NPC: movê-lo. Arrastar chão vazio: seleção com caixa.");
            A("Drag the white handle on a selected NPC: turn it. Hold Shift to snap to 15°.", "Arrastrar el tirador blanco de un NPC seleccionado: girarlo. Mantén Shift para saltos de 15°.", "Arrastar a alça branca de um NPC selecionado: girá-lo. Segure Shift para saltos de 15°.");
            A("Right drag or middle drag: pan. Wheel: zoom. Right click: menu.", "Arrastrar con botón derecho o central: desplazar la vista. Rueda: zoom. Clic derecho: menú.", "Arrastar com botão direito ou do meio: mover a vista. Roda: zoom. Clique direito: menu.");
            A("Arrow keys: move selection by 10 (with Shift: 100).", "Flechas: mover la selección 10 (con Shift: 100).", "Setas: mover a seleção em 10 (com Shift: 100).");
            A("Q / E: turn selected NPCs by 5° (with Shift: 45°).", "Q / E: girar los NPCs seleccionados 5° (con Shift: 45°).", "Q / E: girar os NPCs selecionados em 5° (com Shift: 45°).");
            A("Del: delete. Ctrl+D: duplicate. Ctrl+A: select all flags.", "Supr: eliminar. Ctrl+D: duplicar. Ctrl+A: seleccionar todas las banderas.", "Del: excluir. Ctrl+D: duplicar. Ctrl+A: selecionar todas as bandeiras.");
            A("Ctrl+Z / Ctrl+Y: undo and redo. Ctrl+S: save. F: fit. Esc: back to Select.", "Ctrl+Z / Ctrl+Y: deshacer y rehacer. Ctrl+S: guardar. F: ajustar. Esc: volver a Seleccionar.", "Ctrl+Z / Ctrl+Y: desfazer e refazer. Ctrl+S: salvar. F: ajustar. Esc: voltar para Selecionar.");
            A("Map positions are measured from this server's own map files and kept in the PriTaTor folder.", "Las posiciones de los mapas se miden a partir de los archivos de mapa de este servidor y se guardan en la carpeta PriTaTor.", "As posições dos mapas são medidas a partir dos arquivos de mapa deste servidor e guardadas na pasta PriTaTor.");
            A("The server reads its files at start-up: reload it after saving.", "El servidor lee sus archivos al iniciar: reinícialo después de guardar.", "O servidor lê seus arquivos ao iniciar: reinicie-o depois de salvar.");
            A("Credits", "Créditos", "Créditos");
            A("Sumomo created PriTaTor in 2008 and wrote every version up to PriTaTor 2.", "Sumomo creó PriTaTor en 2008 y escribió todas las versiones hasta PriTaTor 2.", "Sumomo criou o PriTaTor em 2008 e escreveu todas as versões até o PriTaTor 2.");
            A("bobsobol kept the 1.8 downloads and source available and helped with the game's text encoding.", "bobsobol mantuvo disponibles las descargas y el código de la 1.8 y ayudó con la codificación de texto del juego.", "bobsobol manteve disponíveis os downloads e o código da 1.8 e ajudou com a codificação de texto do jogo.");
            A("MysteryPT made the map references that shipped with PriTaTor 2.", "MysteryPT hizo las referencias de mapas que venían con PriTaTor 2.", "MysteryPT fez as referências de mapas que vinham com o PriTaTor 2.");

            // saving and files
            A("Saving failed:", "Error al guardar:", "Falha ao salvar:");
            A("{0} changed on disk since you opened it. Overwrite?", "{0} cambió en el disco desde que lo abriste. ¿Sobrescribir?", "{0} mudou no disco desde que você o abriu. Sobrescrever?");
            A("Saved {0}. Reload the server for this to take effect.", "Se guardó {0}. Reinicia el servidor para que surta efecto.", "{0} salvo. Reinicie o servidor para que tenha efeito.");
            A("Deleted {0}. Reload the server for this to take effect.", "Se eliminó {0}. Reinicia el servidor para que surta efecto.", "{0} excluído. Reinicie o servidor para que tenha efeito.");
            A("Saved {0}. Reload the server for these to take effect. A copy of each old file is in {1}.", "Se guardó {0}. Reinicia el servidor para que surtan efecto. Hay una copia de cada archivo anterior en {1}.", "{0} salvo. Reinicie o servidor para que tenham efeito. Uma cópia de cada arquivo antigo está em {1}.");
            A("Nothing needed saving.", "No hacía falta guardar nada.", "Nada precisava ser salvo.");
            A("Save your changes to {0}?", "¿Guardar los cambios en {0}?", "Salvar as alterações em {0}?");
            A("Delete {0}?\n\nA copy goes in {1}.", "¿Eliminar {0}?\n\nSe guarda una copia en {1}.", "Excluir {0}?\n\nUma cópia vai para {1}.");
            A("This file doesn't link to a name file.", "Este archivo no está vinculado a un archivo de nombres.", "Este arquivo não está vinculado a um arquivo de nomes.");
            A("The linked file isn't there:\n{0}", "El archivo vinculado no está:\n{0}", "O arquivo vinculado não está lá:\n{0}");
            A("Pick a file to copy first.", "Primero elige un archivo para copiar.", "Primeiro escolha um arquivo para copiar.");
            A("This type|*{0}", "Este tipo|*{0}", "Este tipo|*{0}");
            A("New copy of {0}", "Nueva copia de {0}", "Nova cópia de {0}");
            A("This file holds binary data, so it can't be edited as text.", "Este archivo contiene datos binarios, así que no se puede editar como texto.", "Este arquivo contém dados binários, então não pode ser editado como texto.");
            A("Couldn't open {0}:\n{1}", "No se pudo abrir {0}:\n{1}", "Não foi possível abrir {0}:\n{1}");
            A("There's no ground under this NPC.", "No hay suelo bajo este NPC.", "Não há chão sob este NPC.");
            A("Select an NPC first.", "Primero selecciona un NPC.", "Primeiro selecione um NPC.");
            A("This NPC's file isn't there:\n{0}", "El archivo de este NPC no está:\n{0}", "O arquivo deste NPC não está lá:\n{0}");
            A("This NPC's file has unsaved changes on the NPCs tab. Save them there first.", "El archivo de este NPC tiene cambios sin guardar en la pestaña NPCs. Guárdalos allí primero.", "O arquivo deste NPC tem alterações não salvas na aba NPCs. Salve-as lá primeiro.");
            A("This map's flag file is full ({0} slots). Delete a flag first.", "El archivo de banderas de este mapa está lleno ({0} espacios). Elimina una bandera primero.", "O arquivo de bandeiras deste mapa está cheio ({0} espaços). Exclua uma bandeira primeiro.");
            A("This map's NPC file is full ({0} slots). Delete an NPC first.", "El archivo de NPCs de este mapa está lleno ({0} espacios). Elimina un NPC primero.", "O arquivo de NPCs deste mapa está cheio ({0} espaços). Exclua um NPC primeiro.");
            A("Couldn't save PriTaTor.ini:\n{0}", "No se pudo guardar PriTaTor.ini:\n{0}", "Não foi possível salvar o PriTaTor.ini:\n{0}");
            A("Couldn't read that image:\n{0}", "No se pudo leer esa imagen:\n{0}", "Não foi possível ler essa imagem:\n{0}");
            A("No other map has spawn data to copy.", "Ningún otro mapa tiene datos de aparición para copiar.", "Nenhum outro mapa tem dados de surgimento para copiar.");
            A("These files changed on disk since you opened the map, and saving will overwrite them:\n\n{0}\n\nOverwrite?", "Estos archivos cambiaron en el disco desde que abriste el mapa, y al guardar se sobrescribirán:\n\n{0}\n\n¿Sobrescribir?", "Estes arquivos mudaram no disco desde que você abriu o mapa, e salvar os sobrescreverá:\n\n{0}\n\nSobrescrever?");
            A("Most of this map's flags and NPCs are off the map. These amounts would bring them back: the nearest round move to the map's middle.", "La mayoría de las banderas y NPCs de este mapa están fuera del mapa. Estas cantidades los devolverían: el desplazamiento redondo más cercano al centro del mapa.", "A maioria das bandeiras e NPCs deste mapa está fora do mapa. Estes valores os trariam de volta: o deslocamento redondo mais próximo do meio do mapa.");

            // shop editor and dialogs
            A("Add to shop  ▶", "Añadir a la tienda  ▶", "Adicionar à loja  ▶");
            A("Search by name or code", "Buscar por nombre o código", "Buscar por nome ou código");
            A("Remove", "Quitar", "Remover");
            A("Move up", "Subir", "Mover para cima");
            A("Move down", "Bajar", "Mover para baixo");
            A("Move to tab…", "Mover a la pestaña…", "Mover para a aba…");
            A("What this NPC sells", "Qué vende este NPC", "O que este NPC vende");
            A("All items ({0})", "Todos los items ({0})", "Todos os itens ({0})");
            A("(no item file)", "(sin archivo de item)", "(sem arquivo de item)");
            A("{0} items on this tab. The biggest tab on the test server holds 31, so more than {1} may not show in game.", "{0} Items en esta pestaña. La pestaña más grande del servidor de pruebas tiene 31, así que más de {1} podrían no verse en el juego.", "{0} itens nesta aba. A maior aba do servidor de teste tem 31, então mais de {1} podem não aparecer no jogo.");
            A("Each section is a tab in the NPC's shop, and any item can go on any tab. Items already on this tab are grey on the left.", "Cada sección es una pestaña de la tienda del NPC, y cualquier objeto puede ir en cualquiera. Los Items que ya están en esta pestaña salen en gris a la izquierda.", "Cada seção é uma aba da loja do NPC, e qualquer item pode ir em qualquer aba. Os itens que já estão nesta aba aparecem em cinza à esquerda.");
            A("Shop: {0}", "Tienda: {0}", "Loja: {0}");
            A("Weapons", "Armas", "Armas");
            A("Armour", "Armaduras", "Armaduras");
            A("Goods", "Artículos", "Artigos");
            A("Choose an NPC", "Elegir un NPC", "Escolher um NPC");
            A("Place NPC", "Colocar NPC", "Colocar NPC");
            A("Search name or file", "Buscar nombre o archivo", "Buscar nome ou arquivo");
            A("Monster flags", "Banderas de monstruos", "Bandeiras de monstros");
            A("Monster list", "Lista de monstruos", "Lista de monstros");
            A("Shift everything on this map", "Desplazar todo en este mapa", "Deslocar tudo neste mapa");
            A("Move X by", "Mover X en", "Mover X em");
            A("Move Z by", "Mover Z en", "Mover Z em");
            A("Shift", "Desplazar", "Deslocar");
            A("Import", "Importar", "Importar");
            A("Import onto {0}", "Importar en {0}", "Importar em {0}");
            A("Replace what's on this map", "Reemplazar lo que hay en este mapa", "Substituir o que há neste mapa");
            A("Add to what's on this map", "Añadir a lo que hay en este mapa", "Adicionar ao que há neste mapa");
            A("Copy from", "Copiar desde", "Copiar de");
            A("Bring over", "Traer", "Trazer");
            A("{0} has {1} flags and {2} NPCs. At this offset {3} would land outside {4}.", "{0} tiene {1} banderas y {2} NPCs. Con este desplazamiento {3} quedarían fuera de {4}.", "{0} tem {1} bandeiras e {2} NPCs. Com este deslocamento {3} ficariam fora de {4}.");

            // item groups
            A("Accessories", "Accesorios", "Acessórios");
            A("Costumes", "Disfraces", "Trajes");
            A("Potions", "Pociones", "Poções");
            A("Upgrades", "Mejoras", "Melhorias");
            A("Crystals", "Cristales", "Cristais");
            A("Crafting", "Fabricación", "Fabricação");
            A("Premium", "Premium", "Premium");
            A("Quest and event", "Misiones y eventos", "Missões e eventos");
            A("Axes", "Hachas", "Machados");
            A("Bows", "Arcos", "Arcos");
            A("Claws", "Garras", "Garras");
            A("Daggers", "Dagas", "Adagas");
            A("Hammers", "Martillos", "Martelos");
            A("Javelins", "Jabalinas", "Dardos");
            A("Phantoms", "Phantoms", "Phantoms");
            A("Scythes and spears", "Guadañas y lanzas", "Foices e lanças");
            A("Staffs and wands", "Bastones y varitas", "Cajados e varinhas");
            A("Swords", "Espadas", "Espadas");
            A("Vambraces", "Brazales", "Braçadeiras");
            A("Robes", "Túnicas", "Túnicas");
            A("Boots", "Botas", "Botas");
            A("Gauntlets", "Guantes", "Manoplas");
            A("Shields", "Escudos", "Escudos");
            A("Bracelets", "Bracaletes", "Pulseiras");
            A("Orbs", "Orbes", "Orbes");
            A("Amulets", "Amuletos", "Amuletos");
            A("Rings", "Anillos", "Anéis");
            A("Earrings", "Pendientes", "Brincos");
            A("Life", "Vida", "Vida");
            A("Mana", "Maná", "Mana");
            A("Stamina", "Resistencia", "Vigor");
            A("Sheltoms", "Sheltoms", "Sheltoms");
            A("Force orbs", "Force orbs", "Force orbs");
            A("Bellatra stones", "Piedras Bellatra", "Pedras Bellatra");
            A("Monster crystals", "Cristales de monstruo", "Cristais de monstro");
            A("Ores", "Minerales", "Minérios");
            A("Runes", "Runas", "Runas");
            A("Runestones", "Piedras rúnicas", "Pedras rúnicas");
            A("Recipes", "Recetas", "Receitas");
            A("Ingredients", "Ingredientes", "Ingredientes");
            A("Premium items", "Items premium", "Itens premium");
            A("Boosters and stones", "Potenciadores y piedras", "Boosters e pedras");
            A("Cores", "Núcleos", "Núcleos");
            A("Quest items", "Items de misión", "Itens de missão");
            A("Wings", "Alas", "Asas");
            A("Puzzle pieces", "Piezas de puzle", "Peças de quebra-cabeça");
            A("Seals", "Sellos", "Selos");
            A("Gifts", "Regalos", "Presentes");
            A("Events", "Eventos", "Eventos");
            A("Sweets", "Dulces", "Doces");

            // startup
            A("PriTaTor goes in the game server folder, next to GameServer and Field. Which folder is the server in?", "PriTaTor va en la carpeta del servidor del juego, junto a GameServer y Field. ¿En qué carpeta está el servidor?", "O PriTaTor fica na pasta do servidor do jogo, ao lado de GameServer e Field. Em qual pasta está o servidor?");
            A("That folder has no GameServer\\Field folder.", "Esa carpeta no tiene la carpeta GameServer\\Field.", "Essa pasta não tem a pasta GameServer\\Field.");
            A("Something went wrong:\n\n{0}\n\nDetails are in PriTaTor\\errors.log.", "Algo salió mal:\n\n{0}\n\nLos detalles están en PriTaTor\\errors.log.", "Algo deu errado:\n\n{0}\n\nOs detalhes estão em PriTaTor\\errors.log.");
            SchemaWords(A);
            return d;
        }
    }
}
