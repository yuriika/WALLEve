/**
 * ====================================================================================================
 * CYTOSCAPE.JS MAP INTEGRATION - JavaScript-Seite der EVE Map Visualisierung
 * ====================================================================================================
 *
 * ZWECK:
 * Dieses Modul ist die JavaScript-Seite der Map-Visualisierung. Es:
 * 1. Empfängt Daten von Blazor (über JSInterop)
 * 2. Initialisiert und verwaltet die Cytoscape.js Graph-Instanz
 * 3. Rendert Nodes (Systeme/Regionen) und Edges (Stargates/Connections)
 * 4. Verwaltet Interaktionen (Pan, Zoom, Label-Visibility)
 *
 * TECHNOLOGIE:
 * - Cytoscape.js v3.30.2 - Graph-Visualisierungs-Library
 * - Preset-Layout - Nodes werden an exakten Positionen platziert (aus Blazor)
 * - Canvas-Rendering - Performant für 100+ Nodes
 *
 * KOMMUNIKATION MIT BLAZOR:
 * Blazor ruft diese Funktionen auf:
 * - cytoscapeMap.init(containerId, nodes, edges) - Erstmalige Initialisierung
 * - cytoscapeMap.update(nodes, edges) - Update bei View-Wechsel
 * - cytoscapeMap.highlightNode(nodeId) - Markiere aktuelles System
 * - cytoscapeMap.dispose() - Cleanup beim Verlassen der Page
 *
 * DATA FORMAT (von Blazor):
 * nodes: [
 *   {
 *     data: { id: "30000142", label: "Jita" },
 *     position: { x: 450.5, y: 680.2 },
 *     classes: "highsec"
 *   },
 *   ...
 * ]
 * edges: [
 *   {
 *     data: {
 *       source: "30000142",
 *       target: "30000144",
 *       crossRegion: false
 *     }
 *   },
 *   ...
 * ]
 */

// ====================================================================================================
// GLOBAL NAMESPACE - window.cytoscapeMap
// ====================================================================================================
// Alle Funktionen werden unter window.cytoscapeMap exponiert,
// damit Blazor (C#) sie via JSInterop aufrufen kann.

window.cytoscapeMap = {

    // ================================================================================================
    // STATE - Cytoscape-Instanz wird hier gespeichert
    // ================================================================================================
    /**
     * Die Cytoscape.js Graph-Instanz.
     * Wird in init() erstellt und in dispose() zerstört.
     * @type {cytoscape.Core|null}
     */
    cy: null,

    // ================================================================================================
    // SET MAP CANVAS REF - Speichere DotNetObjectReference für Callbacks
    // ================================================================================================
    /**
     * Speichert die DotNetObjectReference für spätere Callbacks (Tooltip-Events).
     * Muss vor init() aufgerufen werden!
     *
     * @param {Object} dotNetRef - DotNetObjectReference aus Blazor
     */
    setMapCanvasRef: function(dotNetRef) {
        window.cytoscapeMapCanvasRef = dotNetRef;
        console.log('✓ DotNetObjectReference registered for callbacks');
    },

    // ================================================================================================
    // INITIALIZATION - Erstmalige Einrichtung der Map
    // ================================================================================================
    /**
     * Initialisiert die Cytoscape Map (wird nur einmal aufgerufen).
     *
     * ABLAUF:
     * 1. Finde DOM-Container (HTML <div id="cy-map">)
     * 2. Konvertiere Daten in Cytoscape-Format
     * 3. Erstelle Cytoscape-Instanz mit Styling und Layout
     * 4. Registriere Event-Handler (Click, Zoom)
     * 5. Führe Auto-Fit aus (zentriere und zoome Map)
     *
     * WIRD AUFGERUFEN VON:
     * Blazor MapCanvas.razor -> InitializeMapAsync() -> JS.InvokeVoidAsync("cytoscapeMap.init", ...)
     *
     * @param {string} containerId - DOM-ID des Containers (z.B. "cy-map")
     * @param {Array} nodes - Array von Node-Objekten (von Blazor)
     * @param {Array} edges - Array von Edge-Objekten (von Blazor)
     */
    init: function(containerId, nodes, edges) {
        console.log('=== Cytoscape Map Initialization ===');
        console.log('Container ID:', containerId);
        console.log('Nodes:', nodes?.length);
        console.log('Edges:', edges?.length);

        // ========================================================================================
        // SCHRITT 1: Finde DOM-Container
        // ========================================================================================
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('❌ Container not found:', containerId);
            return;
        }
        console.log('✓ Container found:', container);

        // ========================================================================================
        // SCHRITT 2: Konvertiere Daten in Cytoscape-Format
        // ========================================================================================
        // Cytoscape erwartet: { nodes: [...], edges: [...] }
        // Blazor liefert: nodes[], edges[]
        // Wir müssen hier:
        // - data, position, classes von Blazor-Objekten extrahieren
        // - Für Edges eine eindeutige ID generieren (source-target)
        const elements = {
            nodes: (nodes || []).map(n => ({
                data: n.data,           // { id: "123", label: "Jita" }
                position: n.position,   // { x: 450.5, y: 680.2 }
                classes: n.classes || '' // "highsec" | "lowsec" | "nullsec" | "region"
            })),
            edges: (edges || []).map(e => ({
                data: {
                    // Eindeutige ID für Edge (Cytoscape-Requirement)
                    id: `${e.data.source}-${e.data.target}`,
                    source: e.data.source,       // z.B. "30000142"
                    target: e.data.target,       // z.B. "30000144"
                    crossRegion: e.data.crossRegion || false
                }
            }))
        };

        console.log('✓ Elements prepared:', {
            nodes: elements.nodes.length,
            edges: elements.edges.length
        });

        // ========================================================================================
        // SCHRITT 3: Erstelle Cytoscape-Instanz
        // ========================================================================================
        this.cy = cytoscape({
            container: container,  // HTML-Element
            elements: elements,    // { nodes: [...], edges: [...] }

            // ====================================================================================
            // STYLING - CSS für Nodes und Edges
            // ====================================================================================
            // Cytoscape nutzt CSS-ähnliche Selektoren für Styling:
            // - selector: 'node' = Alle Nodes
            // - selector: 'node.highsec' = Alle Nodes mit class="highsec"
            // - selector: 'edge[crossRegion]' = Alle Edges mit data.crossRegion=true
            style: [
                // --------------------------------------------------------------------------------
                // BASE NODE STYLE - Gilt für alle Nodes
                // --------------------------------------------------------------------------------
                {
                    selector: 'node',
                    style: {
                        // Visuals
                        'background-color': '#666',  // Grau (wird von Security-Farben überschrieben)
                        'width': 18,                 // Node-Größe in Pixeln
                        'height': 18,

                        // Labels
                        'label': 'data(label)',      // Zeige Label aus node.data.label
                        'font-size': '10px',
                        'color': '#fff',             // Weiße Schrift
                        'text-valign': 'bottom',     // Text unter dem Node
                        'text-halign': 'center',     // Text zentriert
                        'text-margin-y': 3,          // 3px Abstand zwischen Node und Text

                        // Text-Outline für bessere Lesbarkeit auf dunklem Hintergrund
                        'text-outline-width': 1.5,
                        'text-outline-color': '#000',

                        // Text-Overflow-Handling
                        'text-max-width': '80px',    // Max-Breite, danach Ellipsis
                        'text-wrap': 'ellipsis',     // "Very Long Name..." statt "Very Long Name"

                        // Zoom-abhängige Schriftgröße
                        'min-zoomed-font-size': 8    // Bei starkem Zoom-Out: min 8px
                    }
                },

                // --------------------------------------------------------------------------------
                // SECURITY STATUS COLORS - Überschreiben von background-color
                // --------------------------------------------------------------------------------
                // EVE Online Security Status:
                // - HighSec (≥0.5): Grün - Sicher
                // - LowSec (0.1-0.5): Gelb - Gefährlich
                // - NullSec (<0.1): Rot - Sehr gefährlich
                {
                    selector: 'node.highsec',
                    style: {
                        'background-color': '#00ff00'  // Hellgrün
                    }
                },
                {
                    selector: 'node.lowsec',
                    style: {
                        'background-color': '#ffff00'  // Gelb
                    }
                },
                {
                    selector: 'node.nullsec',
                    style: {
                        'background-color': '#ff4444'  // Rot
                    }
                },

                // --------------------------------------------------------------------------------
                // CURRENT SYSTEM HIGHLIGHT - Charakter-Position
                // --------------------------------------------------------------------------------
                // Wird durch highlightNode() auf den aktuellen Node angewendet
                {
                    selector: 'node.current-system',
                    style: {
                        'border-width': 4,             // Dicker Border
                        'border-color': '#00b4ff',     // EVE-Blau
                        'border-style': 'solid',
                        'width': 25,                   // Größer als normale Nodes
                        'height': 25
                    }
                },

                // --------------------------------------------------------------------------------
                // CROSS-REGION TARGET NODES - Dummy-Nodes für andere Regionen
                // --------------------------------------------------------------------------------
                // Diese Nodes erscheinen am Map-Rand und repräsentieren Ziel-Regionen
                // für Cross-Region Connections (statt nicht-existierender Target-Systeme)
                {
                    selector: 'node.cross-region-target',
                    style: {
                        'shape': 'vee',                // Halbkreis-ähnliche Form
                        'background-color': '#ff8800', // Orange (Cross-Region Farbe)
                        'width': 50,                   // Größer als normale System-Nodes
                        'height': 50,

                        // Label (Region-Name)
                        'label': 'data(label)',
                        'font-size': '14px',           // Größere Schrift als normale Nodes
                        'font-weight': 'bold',
                        'text-valign': 'center',
                        'text-halign': 'center',
                        'color': '#fff',

                        // Text-Outline für bessere Lesbarkeit
                        'text-outline-width': 2,
                        'text-outline-color': '#000',

                        // Border für zusätzliche Hervorhebung
                        'border-width': 3,
                        'border-color': '#ffaa00',     // Helleres Orange für Border
                        'border-style': 'solid',

                        // Zoom-Verhalten
                        'min-zoomed-font-size': 10
                    }
                },

                // --------------------------------------------------------------------------------
                // BASE EDGE STYLE - Verbindungen zwischen Nodes
                // --------------------------------------------------------------------------------
                {
                    selector: 'edge',
                    style: {
                        'width': 1.5,                  // Liniendicke in Pixeln (erhöht von 1)
                        'line-color': '#88bbdd',       // Hellblau (besser sichtbar auf schwarzem Hintergrund)
                        'curve-style': 'straight',     // Gerade Linien (kein Bezier)
                        'target-arrow-shape': 'none',  // Keine Pfeile (nicht gerichtet)
                        'opacity': 0.7                 // 70% Transparenz (erhöht von 0.4 für bessere Sichtbarkeit)
                    }
                },

                // --------------------------------------------------------------------------------
                // CROSS-REGION EDGES - Verbindungen zwischen Regionen
                // --------------------------------------------------------------------------------
                // Diese Edges sind besonders wichtig (Region-Übergänge)
                // Sie führen zu Dummy-Nodes am Map-Rand und werden als gestrichelte Linien dargestellt
                {
                    selector: 'edge[crossRegion]',
                    style: {
                        'line-color': '#ff8800',       // Orange
                        'width': 2,                    // Dicker als normale Edges (erhöht von 1.5)
                        'opacity': 0.8,                // Weniger transparent (erhöht von 0.6)
                        'line-style': 'dashed',        // Gestrichelte Linie
                        'line-dash-pattern': [8, 4]    // 8px Strich, 4px Lücke
                    }
                }
            ],

            // ====================================================================================
            // LAYOUT - Wie Nodes angeordnet werden
            // ====================================================================================
            // 'preset' = Nodes werden an exakten Positionen platziert (aus node.position)
            // Keine automatische Anordnung (würde EVE-Map-Struktur zerstören)
            layout: {
                name: 'preset',  // Nutze vordefinierte Positionen
                fit: true,       // Zoome so, dass alle Nodes sichtbar sind
                padding: 100     // 100px Rand um den Graph
            },

            // ====================================================================================
            // INTERACTION SETTINGS - User-Interaktion
            // ====================================================================================
            // Pan & Zoom sind aktiviert, aber Node-Dragging ist deaktiviert
            // (würde das Layout kaputt machen)

            // Node-Interaction
            autoungrabify: true,       // ❌ Nodes können NICHT verschoben werden
            autolock: false,           // Nodes können programmatisch bewegt werden (intern)
            autounselectify: false,    // ✅ Nodes können ausgewählt werden (für Events)
            boxSelectionEnabled: false, // ❌ Box-Selection deaktiviert

            // Pan/Zoom
            panningEnabled: true,       // ✅ Map kann verschoben werden (Maus-Drag)
            userPanningEnabled: true,   // ✅ User darf Pan nutzen
            zoomingEnabled: true,       // ✅ Zoom ist aktiviert
            userZoomingEnabled: true,   // ✅ User darf Zoom nutzen (Mausrad)
            minZoom: 0.1,               // Min Zoom-Level (10% = sehr weit raus)
            maxZoom: 5.0,               // Max Zoom-Level (500% = sehr nah ran)
            wheelSensitivity: 0.2       // Mausrad-Sensitivität (0.2 = langsam, smooth)
        });

        console.log('✓ Cytoscape instance created');
        console.log('✓ Cytoscape stats:', {
            nodes: this.cy.nodes().length,
            edges: this.cy.edges().length,
            firstEdge: this.cy.edges().length > 0 ? this.cy.edges()[0].data() : 'none'
        });

        // ========================================================================================
        // SCHRITT 4: Event-Handler registrieren
        // ========================================================================================

        // ------------------------------------------------------------------------------------
        // NODE CLICK EVENT
        // ------------------------------------------------------------------------------------
        // Wird getriggert wenn User auf einen Node klickt
        // TODO: Könnte später Blazor-Callback aufrufen für Navigation
        this.cy.on('tap', 'node', function(evt) {
            const node = evt.target;
            console.log('🖱️ Node clicked:', node.data());
            // Hier könnte man später:
            // - DotNet.invokeMethodAsync() für Blazor-Callback
            // - Navigation zu System-Details
        });

        // ------------------------------------------------------------------------------------
        // NODE HOVER EVENTS - Tooltip anzeigen/verstecken
        // ------------------------------------------------------------------------------------
        // MOUSEOVER: Zeige Tooltip wenn Maus über Node
        this.cy.on('mouseover', 'node', function(evt) {
            const node = evt.target;
            const renderedPos = evt.renderedPosition;

            // Canvas-Position im Viewport
            const canvasRect = evt.cy.container().getBoundingClientRect();

            // Tooltip-Position: Canvas-Position + rendered Position + Offset
            // fixed positioning braucht absolute Viewport-Koordinaten
            const tooltipX = canvasRect.left + renderedPos.x + 20;
            const tooltipY = canvasRect.top + renderedPos.y + 20;

            console.log('🖱️ Node hover:', {
                nodeId: node.id(),
                label: node.data('label'),
                canvasRect: { left: canvasRect.left, top: canvasRect.top },
                renderedPos: { x: renderedPos.x, y: renderedPos.y },
                tooltipPos: { x: tooltipX, y: tooltipY },
                hasRef: !!window.cytoscapeMapCanvasRef
            });

            // Blazor-Callback: Zeige Tooltip
            if (window.cytoscapeMapCanvasRef) {
                window.cytoscapeMapCanvasRef.invokeMethodAsync('ShowTooltip',
                    node.id(),
                    tooltipX,
                    tooltipY
                ).catch(err => {
                    console.error('❌ ShowTooltip failed:', err);
                });
            } else {
                console.warn('⚠️ No DotNetObjectReference available for tooltip');
            }
        });

        // MOUSEOUT: Verstecke Tooltip wenn Maus Node verlässt
        this.cy.on('mouseout', 'node', function(evt) {
            console.log('🖱️ Node mouseout');

            // Blazor-Callback: Verstecke Tooltip
            if (window.cytoscapeMapCanvasRef) {
                window.cytoscapeMapCanvasRef.invokeMethodAsync('HideTooltip')
                    .catch(err => {
                        console.error('❌ HideTooltip failed:', err);
                    });
            }
        });

        // ------------------------------------------------------------------------------------
        // ZOOM EVENT - Zoom-abhängige Label-Sichtbarkeit
        // ------------------------------------------------------------------------------------
        // PROBLEM: Bei vielen Nodes werden Labels unleserlich wenn rausgezoomt
        // LÖSUNG: Blende Labels bei Zoom < 0.4 aus, zeige sie bei Zoom > 0.7
        this.cy.on('zoom', () => {
            const zoom = this.cy.zoom();

            // Dynamische Label-Opacity basierend auf Zoom-Level
            if (zoom < 0.4) {
                // Sehr weit rausgezoomt: Labels komplett ausblenden
                this.cy.nodes().style('text-opacity', 0);
            } else if (zoom < 0.7) {
                // Mittel-Zoom: Labels halbtransparent
                this.cy.nodes().style('text-opacity', 0.5);
            } else {
                // Nah rangezoomt: Labels voll sichtbar
                this.cy.nodes().style('text-opacity', 1);
            }
        });

        // ========================================================================================
        // SCHRITT 5: Auto-Fit und Initial-Zoom
        // ========================================================================================
        // Verzögerung von 100ms, damit Rendering abgeschlossen ist
        setTimeout(() => {
            // Fit: Zoome so, dass alle Nodes sichtbar sind (mit 100px Padding)
            this.cy.fit(null, 100);

            // Center: Zentriere die Map im Viewport
            this.cy.center();

            // Fix für zu starken Zoom-Out bei kleinen Graphs:
            // Falls Zoom < 0.3: Zoome auf 0.5 (bessere Lesbarkeit)
            const currentZoom = this.cy.zoom();
            if (currentZoom < 0.3) {
                this.cy.zoom({
                    level: 0.5,  // Ziel-Zoom-Level
                    renderedPosition: {
                        x: this.cy.width() / 2,   // Zoom-Zentrum: Mitte des Canvas
                        y: this.cy.height() / 2
                    }
                });
            }

            console.log('✓ Initial zoom/fit completed:', {
                zoom: this.cy.zoom(),
                pan: this.cy.pan()
            });
        }, 100);

        console.log('=== Cytoscape Map Initialized Successfully ===');
    },

    // ================================================================================================
    // UPDATE - Map mit neuen Daten aktualisieren
    // ================================================================================================
    /**
     * Aktualisiert die Map mit neuen Nodes und Edges.
     *
     * WANN AUFGERUFEN:
     * - User wechselt View-Mode (Region → System)
     * - User wechselt Region (The Forge → Domain)
     * - User ändert Jump-Radius (5 Jumps → 3 Jumps)
     *
     * ABLAUF:
     * 1. Entferne alle existierenden Nodes und Edges
     * 2. Füge neue Nodes hinzu
     * 3. Füge neue Edges hinzu
     * 4. Führe Auto-Fit aus (zentriere und zoome)
     * 5. Trigger Zoom-Event (für Label-Visibility)
     *
     * UNTERSCHIED zu init():
     * - Nutzt existierende Cytoscape-Instanz (kein neues cytoscape(...))
     * - Schneller als komplette Neu-Initialisierung
     *
     * @param {Array} nodes - Neue Node-Daten
     * @param {Array} edges - Neue Edge-Daten
     */
    update: function(nodes, edges) {
        console.log('=== Cytoscape Map Update ===');
        console.log('Nodes:', nodes?.length);
        console.log('Edges:', edges?.length);

        // ========================================================================================
        // SAFETY CHECK: Ist Cytoscape initialisiert?
        // ========================================================================================
        if (!this.cy) {
            console.error('❌ Cytoscape not initialized! Call init() first.');
            return;
        }

        // ========================================================================================
        // SCHRITT 1: Entferne alle existierenden Elements
        // ========================================================================================
        // elements() gibt alle Nodes und Edges zurück
        // remove() entfernt sie aus dem Graph
        this.cy.elements().remove();
        console.log('✓ Previous elements removed');

        // ========================================================================================
        // SCHRITT 2: Füge neue Nodes hinzu
        // ========================================================================================
        (nodes || []).forEach(n => {
            this.cy.add({
                group: 'nodes',          // "nodes" oder "edges"
                data: n.data,            // { id, label }
                position: n.position,    // { x, y }
                classes: n.classes || '' // CSS-Class für Styling
            });
        });
        console.log('✓ New nodes added:', nodes?.length);

        // ========================================================================================
        // SCHRITT 3: Füge neue Edges hinzu
        // ========================================================================================
        (edges || []).forEach(e => {
            this.cy.add({
                group: 'edges',
                data: {
                    id: `${e.data.source}-${e.data.target}`,  // Eindeutige ID
                    source: e.data.source,                     // Source Node ID
                    target: e.data.target,                     // Target Node ID
                    crossRegion: e.data.crossRegion || false
                }
            });
        });
        console.log('✓ New edges added:', edges?.length);

        // ========================================================================================
        // SCHRITT 4: Auto-Fit und Zoom-Reset
        // ========================================================================================
        setTimeout(() => {
            // Re-fit: Alle neuen Nodes sollen sichtbar sein
            this.cy.fit(null, 100);
            this.cy.center();

            // Zoom-Fix (wie in init())
            const currentZoom = this.cy.zoom();
            if (currentZoom < 0.3) {
                this.cy.zoom({
                    level: 0.5,
                    renderedPosition: {
                        x: this.cy.width() / 2,
                        y: this.cy.height() / 2
                    }
                });
            }

            // Trigger Zoom-Event manuell (für Label-Visibility Update)
            this.cy.trigger('zoom');

            console.log('✓ Re-fit completed:', {
                zoom: this.cy.zoom(),
                pan: this.cy.pan()
            });
        }, 100);

        console.log('=== Cytoscape Map Updated Successfully ===');
    },

    // ================================================================================================
    // HIGHLIGHT NODE - Markiere aktuelles System
    // ================================================================================================
    /**
     * Hebt einen spezifischen Node visuell hervor (z.B. aktuelles Charakter-System).
     *
     * WANN AUFGERUFEN:
     * - Nach init() oder update() von Blazor
     * - CharacterSystemId hat sich geändert
     *
     * ABLAUF:
     * 1. Entferne "current-system" Class von allen Nodes
     * 2. Füge "current-system" Class zum Ziel-Node hinzu
     * 3. Cytoscape wendet automatisch das Styling an (siehe style-Block)
     *
     * @param {string} nodeId - ID des zu markierenden Nodes (z.B. "30000142")
     */
    highlightNode: function(nodeId) {
        if (!this.cy) return;

        // Entferne Highlight von allen Nodes
        // removeClass() entfernt CSS-Class von allen Nodes
        this.cy.nodes().removeClass('current-system');

        // Finde Ziel-Node und füge Highlight hinzu
        const node = this.cy.getElementById(nodeId);
        if (node) {
            node.addClass('current-system');
            console.log('✓ Node highlighted:', nodeId);
        } else {
            console.warn('⚠️ Node not found for highlighting:', nodeId);
        }
    },

    // ================================================================================================
    // DISPOSE - Cleanup beim Verlassen der Page
    // ================================================================================================
    /**
     * Gibt Cytoscape-Ressourcen frei (Memory-Leak-Prevention).
     *
     * WANN AUFGERUFEN:
     * - User navigiert weg von Map-Page
     * - Blazor Component wird disposed (DisposeAsync() in MapCanvas.razor)
     *
     * WICHTIG:
     * Cytoscape hält viele DOM-Referenzen und Event-Listener.
     * destroy() entfernt alle und gibt Memory frei.
     */
    dispose: function() {
        if (this.cy) {
            this.cy.destroy();  // Zerstöre Cytoscape-Instanz
            this.cy = null;      // Setze Referenz auf null
            console.log('✓ Cytoscape instance disposed');
        }
    }
};

// ====================================================================================================
// EXPERIMENTIER-TIPPS
// ====================================================================================================
/**
 * Du möchtest mit dem Code experimentieren? Hier sind einige Ideen:
 *
 * 1. NODE-GRÖSSEN ändern:
 *    - Zeile 64-65: 'width' und 'height' ändern (z.B. 25 für größere Nodes)
 *    - Zeile 104-105: current-system 'width'/'height' anpassen
 *
 * 2. FARBEN anpassen:
 *    - Zeile 82-95: Security-Farben ändern (z.B. '#00ff00' → '#00cc00')
 *    - Zeile 113: Edge-Farbe ändern (z.B. '#5577bb' → '#ffffff')
 *
 * 3. ZOOM-VERHALTEN:
 *    - Zeile 147-149: minZoom/maxZoom anpassen (z.B. minZoom: 0.5 für weniger Zoom-Out)
 *    - Zeile 166-172: Zoom-Schwellenwerte für Labels ändern
 *
 * 4. LABEL-STYLING:
 *    - Zeile 66: 'font-size' ändern (z.B. '12px' für größere Labels)
 *    - Zeile 67-69: Label-Position ändern (text-valign: 'top'/'center'/'bottom')
 *
 * 5. EDGE-STYLING:
 *    - Zeile 114: curve-style ändern ('bezier', 'unbundled-bezier', 'haystack')
 *    - Zeile 112: width ändern für dickere Linien
 *
 * 6. ANIMATIONS hinzufügen:
 *    - Nach Zeile 210 einfügen: node.animate({ position: {...}, duration: 500 })
 *    - Smooth Transitions bei Node-Hinzufügen
 *
 * 7. TOOLTIPS:
 *    - Nach Zeile 155 Popper.js integrieren für schöne Tooltips
 *    - Zeige System-Details, Security, Kills, etc.
 *
 * Viel Spaß beim Experimentieren! 🚀
 */
