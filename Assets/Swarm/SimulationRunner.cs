using System;
using System.IO;
using System.Text;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading;
using Newtonsoft.Json;
using LLM2Swarm.Experiments;

namespace LLM2Swarm
{
    public sealed class SimulationRunner : MonoBehaviour
    {
        Simulation simulation;
        bool running;
        double accumulator;
        string seed = "42", status = "", missionPath;
        Vector2 missionScroll, jsonScroll;
        string jsonDraft = "";
        string instruction = "";
        bool showJson;
        Mission proposedMission;
        MissionIntent proposedIntent, activeIntent;
        string interpretationMessage = "", proposalInstruction = "", proposalEvidence = "", activeInstruction = "", activeEvidence = "";
        string inferenceRoot, inferenceAssets, inferenceLogs;
        bool interpreting;
        CancellationTokenSource interpretationCancellation;
        LocalModel selectedModel=LocalModel.Ministral3B;
        SearchEnvironment experiment;
        ISearchStrategy experimentStrategy;
        SearchObservation experimentObservation;
        double experimentAccumulator;
        int selectedStrategy;
        public int gpuLayers = 99;
        GUIStyle titleStyle, sectionStyle, textStyle, smallStyle, buttonStyle, jsonStyle, instructionStyle;
        [Tooltip("Optional manually configured mission JSON")]
        public TextAsset missionJson;
        readonly Color[] colors = { new Color(.2f,.8f,1), new Color(1,.7f,.2f), new Color(.9f,.4f,.8f) };
        static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name == "SampleScene" && FindAnyObjectByType<SimulationRunner>() == null)
                new GameObject("Manual search simulation").AddComponent<SimulationRunner>();
        }
        void Awake()
        {
            inferenceRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName,"LocalInference");
            inferenceAssets = Path.Combine(Application.streamingAssetsPath,"Inference");
            inferenceLogs = Path.Combine(Application.persistentDataPath,"Interpretations");
            missionPath = Path.Combine(Application.persistentDataPath,"manual-mission.json");
            ResetMission(missionJson == null ? Mission.Sweep() : MissionJson.Parse(missionJson.text));
        }
        void ResetMission(Mission mission)
        {
            experiment=null;experimentStrategy=null;experimentObservation=null;experimentAccumulator=0;
            if (simulation == null || !ReferenceEquals(mission,simulation.Mission)) { activeIntent=null; activeInstruction=""; activeEvidence=""; }
            simulation = new Simulation(mission); running = false; accumulator = 0;
            seed = mission.seed.ToString(); jsonDraft = JsonUtility.ToJson(mission,true); status = "Misión preparada";
            proposedMission = null;
            proposedIntent = null;
        }
        void OnDisable() { interpretationCancellation?.Cancel(); }
        async void InterpretInstruction()
        {
            if (interpreting || running) return;
            proposedMission=null; proposedIntent=null; interpretationMessage="";
            interpreting=true;
            interpretationCancellation = new CancellationTokenSource();
            var cancellation = interpretationCancellation;
            string submitted = instruction;
            try
            {
                var result = await LocalInterpreter.InterpretAsync(inferenceRoot,inferenceAssets,inferenceLogs,submitted,gpuLayers,cancellation.Token,
                    LocalInterpreter.ModelFileFor(selectedModel),LocalInterpreter.ManifestFor(selectedModel),LocalInterpreter.ChatTemplateFor(selectedModel));
                if (cancellation.IsCancellationRequested || this == null) return;
                interpretationMessage=result.response.message;
                proposalInstruction=submitted; proposalEvidence=result.requestDirectory;
                if (result.response.status == "proposal")
                {
                    proposedIntent=result.response.mission;
                    proposedMission=MissionPlanner.Plan(Scenario.From(simulation.Mission),proposedIntent);
                    jsonDraft=JsonConvert.SerializeObject(result.response,Formatting.Indented);
                    status=$"Propuesta local · {result.elapsedSeconds:F1} s · pendiente de confirmar";
                }
                else status=result.response.status == "clarification" ? "Necesita aclaración" : "Petición no soportada";
                missionScroll=Vector2.zero;
            }
            catch (OperationCanceledException) { if (this != null) status="Interpretación cancelada"; }
            catch (Exception e) { if (this != null) { proposedMission=null; proposedIntent=null; status=e.Message; } }
            finally { if (this != null) interpreting=false; cancellation.Dispose(); if (ReferenceEquals(interpretationCancellation,cancellation)) interpretationCancellation=null; }
        }
        void PrepareProposal()
        {
            var intent=proposedIntent; string source=intent == null ? "" : proposalInstruction, evidence=intent == null ? "" : proposalEvidence;
            ResetMission(proposedMission);
            activeIntent=intent; activeInstruction=source; activeEvidence=evidence;
        }
        void Update()
        {
            if (!running || simulation.Finished) return;
            if(experiment!=null)
            {
                experimentAccumulator+=Time.unscaledDeltaTime;
                while(experimentAccumulator>=1&&!experiment.Finished)
                {
                    experimentAccumulator-=1;experiment.Step(experimentStrategy.Decide(experiment.Observe()));experimentObservation=experiment.Observe();
                }
                if(experiment.Finished){running=false;status=$"Experimento {experimentStrategy.Id} · {experiment.Metrics().finishReason}";}
                return;
            }
            accumulator += Time.unscaledDeltaTime;
            while (accumulator >= simulation.Mission.stepSeconds && !simulation.Finished)
            { accumulator -= simulation.Mission.stepSeconds; simulation.Step(); }
            if (simulation.Finished) { running = false; Export(); }
        }
        void Attempt(Action action) { try { action(); } catch (Exception e) { status = e.Message; Debug.LogException(e); } }
        void StartExperiment()
        {
            var intent=new MissionIntent{requiredFindings=simulation.Mission.requiredFindings,maxSeconds=simulation.Mission.maxSeconds,areas=new[]{"all"}};
            experimentStrategy=selectedStrategy==1 ? new SystematicStrategy(false) : selectedStrategy==2 ? new SystematicStrategy(true) :
                new PpoSeed47Strategy(File.ReadAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Experiments","results","ppo","full","47","policy.json")));
            experiment=new SearchEnvironment(Scenario.From(simulation.Mission),intent);experimentObservation=experiment.Observe();experimentStrategy.Reset(experimentObservation);experimentAccumulator=0;running=true;status="Experimento Fast 2D · "+experimentStrategy.Id;
        }
        void Export()
        {
            Attempt(() => {
                string directory = Path.Combine(Application.persistentDataPath,"Runs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"mission.json"),JsonUtility.ToJson(simulation.Mission,true));
                File.WriteAllText(Path.Combine(directory,"scenario.json"),JsonConvert.SerializeObject(Scenario.From(simulation.Mission),Formatting.Indented));
                File.WriteAllText(Path.Combine(directory,"intent.json"),JsonConvert.SerializeObject(activeIntent,Formatting.Indented));
                File.WriteAllText(Path.Combine(directory,"instruction.txt"),activeInstruction);
                File.WriteAllText(Path.Combine(directory,"interpretation-evidence.txt"),activeEvidence);
                File.WriteAllText(Path.Combine(directory,"phases.json"),JsonConvert.SerializeObject(simulation.PhaseEvents,Formatting.Indented));
                var csv = new StringBuilder("tick,seconds,coverage_fraction,found\n");
                foreach (var s in simulation.Samples) csv.AppendFormat(CultureInfo.InvariantCulture,"{0},{1:R},{2:R},{3}\n",s.tick,s.seconds,s.coverage,s.found);
                File.WriteAllText(Path.Combine(directory,"metrics.csv"),csv.ToString());
                csv = new StringBuilder("target,drone,tick,seconds,x,y\n");
                foreach (var f in simulation.Findings) csv.AppendFormat(CultureInfo.InvariantCulture,"{0},{1},{2},{3:R},{4:R},{5:R}\n",f.target,f.drone,f.tick,f.seconds,f.position.x,f.position.y);
                File.WriteAllText(Path.Combine(directory,"findings.csv"),csv.ToString());
                File.WriteAllText(Path.Combine(directory,"summary.json"),JsonUtility.ToJson(new Summary {
                    finishReason=simulation.FinishReason ?? "snapshot", seconds=simulation.Seconds,
                    coverage=simulation.Coverage, found=simulation.Findings.Count, total=simulation.TargetCount },true));
                status = "Exportado: " + directory;
                Debug.Log(status);
            });
        }
        [Serializable] class Summary { public string finishReason; public double seconds,coverage; public int found,total; }
        static void Box(Rect rect, Color color) { GUI.color = color; GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color = Color.white; }
        void OnGUI()
        {
            if (simulation == null) return;
            EnsureStyles();
            Matrix4x4 original = GUI.matrix;
            float uiScale = Mathf.Min(1,Mathf.Min(Screen.width/1060f,Screen.height/700f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
            float width = Screen.width/uiScale, height = Screen.height/uiScale;
            Box(new Rect(0,0,width,height),new Color(.035f,.05f,.08f));
            GUI.Label(new Rect(24,16,600,32),"LLM2Swarm  /  Centro de misión",titleStyle);
            GUI.Label(new Rect(24,52,650,24),"Búsqueda cooperativa · 3 drones · Vista cenital",smallStyle);
            float panelWidth = Mathf.Clamp(width*.30f,350,430);
            var mapPanel = new Rect(20,90,width-panelWidth-52,height-176);
            var missionPanel = new Rect(width-panelWidth-20,90,panelWidth,height-110);
            Box(mapPanel,new Color(.065f,.085f,.12f));
            GUI.Label(new Rect(mapPanel.x+16,mapPanel.y+12,mapPanel.width-32,26),"SIMULACIÓN EN TIEMPO REAL",sectionStyle);
            if(experiment==null) DrawMap(new Rect(mapPanel.x+20,mapPanel.y+56,mapPanel.width-40,mapPanel.height-104));
            else DrawExperimentMap(new Rect(mapPanel.x+20,mapPanel.y+56,mapPanel.width-40,mapPanel.height-104));
            GUI.Label(new Rect(mapPanel.x+16,mapPanel.yMax-36,mapPanel.width-32,26),"Verde: explorado    Blanco: hallazgo    Líneas: rutas",smallStyle);
            float buttonWidth = (mapPanel.width-24)/3;
            GUI.enabled = !simulation.Finished && !interpreting;
            if (ActionButton(new Rect(mapPanel.x,mapPanel.yMax+16,buttonWidth,52),running ? "Pausar" : simulation.Tick > 0 ? "Continuar" : "Iniciar",new Color(.12f,.62f,.34f))) running = !running;
            GUI.enabled = !interpreting;
            if (ActionButton(new Rect(mapPanel.x+buttonWidth+12,mapPanel.yMax+16,buttonWidth,52),"Reiniciar",new Color(.92f,.48f,.12f))) ResetMission(simulation.Mission);
            GUI.enabled = !simulation.Finished;
            if (ActionButton(new Rect(mapPanel.x+2*(buttonWidth+12),mapPanel.yMax+16,buttonWidth,52),"Interrumpir",new Color(.8f,.19f,.22f)))
            {
                if(experiment!=null){running=false;status="Experimento interrumpido";}else{simulation.Interrupt();running=false;Export();}
            }
            GUI.enabled = true;
            DrawMissionPanel(missionPanel);
            GUI.matrix = original;
        }
        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize=24, fontStyle=FontStyle.Bold };
            sectionStyle = new GUIStyle(GUI.skin.label) { fontSize=17, fontStyle=FontStyle.Bold };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize=16, wordWrap=true };
            smallStyle = new GUIStyle(textStyle) { fontSize=13 };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize=17, fontStyle=FontStyle.Bold, wordWrap=true };
            jsonStyle = new GUIStyle(GUI.skin.textArea) { fontSize=13, wordWrap=true };
            instructionStyle = new GUIStyle(GUI.skin.textArea) { fontSize=16, wordWrap=true, padding=new RectOffset(8,8,8,8) };
        }
        bool ActionButton(Rect rect, string label, Color color)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool pressed = GUI.Button(rect,label,buttonStyle);
            GUI.backgroundColor = previous;
            return pressed;
        }
        string MissionState()
        {
            switch (simulation.FinishReason)
            {
                case "success": return "Completada · objetivo alcanzado";
                case "search_exhausted": return "Exploración agotada · objetivo pendiente";
                case "time_limit": return "Finalizada · límite temporal";
                case "interrupted": return "Interrumpida";
                default: return running ? "En ejecución" : simulation.Tick == 0 ? "Preparada" : "En pausa";
            }
        }
        void DrawMissionPanel(Rect panel)
        {
            Box(panel,new Color(.085f,.105f,.15f));
            var m = simulation.Mission;
            GUILayout.BeginArea(new Rect(panel.x+16,panel.y+12,panel.width-32,318));
            GUILayout.Label("INSTRUCCIÓN DE MISIÓN",sectionStyle);
            GUI.enabled = !interpreting;
            string editedInstruction = GUILayout.TextArea(instruction,instructionStyle,GUILayout.Height(68));
            if (editedInstruction != instruction)
            {
                instruction=editedInstruction;
                if (proposedIntent != null) { proposedMission=null; proposedIntent=null; }
                interpretationMessage="";
            }
            if (string.IsNullOrWhiteSpace(instruction))
                GUILayout.Label("Ej.: Encuentra dos objetivos en menos de 120 s.",smallStyle);
            else GUILayout.Label(interpreting ? "Interpretando en este equipo…" : "Revisa la propuesta antes de preparar",smallStyle);
            GUILayout.Label("MODELO LOCAL",smallStyle);
            selectedModel=(LocalModel)GUILayout.SelectionGrid((int)selectedModel,new[]{"Ministral 3B","Qwen3 4B"},2,GUILayout.Height(28));
            bool modelAvailable=LocalInterpreter.Available(inferenceRoot,selectedModel);
            GUI.enabled = !running && !interpreting && !string.IsNullOrWhiteSpace(instruction) && modelAvailable;
            if (GUILayout.Button("Interpretar instrucción",buttonStyle,GUILayout.Height(36))) InterpretInstruction();
            GUI.enabled = true;
            if (interpreting) { if (GUILayout.Button("Cancelar interpretación")) interpretationCancellation?.Cancel(); }
            else GUILayout.Label(modelAvailable ? LocalInterpreter.DisplayName(selectedModel)+" · local · sin envío a Internet" : "Modelo pendiente: revisa LocalInference/README.md",smallStyle);
            GUILayout.Label("ESTRATEGIA FAST 2D",smallStyle);
            selectedStrategy=GUILayout.SelectionGrid(selectedStrategy,new[]{"Manual","Baseline A","Baseline B","PPO seed 47"},2,GUILayout.Height(48));
            GUI.enabled=!running&&!interpreting&&selectedStrategy>0;
            if(GUILayout.Button("Iniciar estrategia seleccionada",buttonStyle,GUILayout.Height(32))) Attempt(StartExperiment);
            GUI.enabled=true;
            GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(panel.x+16,panel.y+338,panel.width-32,panel.height-390));
            missionScroll = GUILayout.BeginScrollView(missionScroll);
            if (!string.IsNullOrEmpty(interpretationMessage))
            {
                GUILayout.Label("RESPUESTA DEL INTÉRPRETE",sectionStyle);
                GUILayout.Label(interpretationMessage,textStyle);
                if (proposedIntent != null) GUILayout.Label("RESUMEN VALIDADO\n"+proposedIntent.Summary(),textStyle);
                GUILayout.Space(10);
            }
            GUILayout.Label("MISIÓN ACTIVA · " + MissionState(),sectionStyle);
            GUILayout.Label("OBJETIVO",sectionStyle);
            GUILayout.Label($"Encontrar {m.requiredFindings} objetos distintos",textStyle);
            GUILayout.Label($"Encontrados: {simulation.Findings.Count}   ·   Pendientes: {Math.Max(0,m.requiredFindings-simulation.Findings.Count)}",textStyle);
            GUILayout.Space(10);
            GUILayout.Label("TIEMPO Y COBERTURA",sectionStyle);
            GUILayout.Label($"Transcurrido: {simulation.Seconds:F1} s / {m.maxSeconds:F1} s\nRestante: {Math.Max(0,m.maxSeconds-simulation.Seconds):F1} s\nCobertura: {simulation.Coverage:P1}",textStyle);
            GUILayout.Space(10);
            GUILayout.Label("REGIÓN Y PRIORIDADES",sectionStyle);
            GUILayout.Label($"Región: {m.width} × {m.height} m\nFase actual: {MissionIntent.AreaName(simulation.ActiveArea)}",textStyle);
            GUILayout.Label(activeIntent == null ? "Misión manual con rutas explícitas." : activeIntent.Summary(),smallStyle);
            GUILayout.Space(10);
            GUILayout.Label("EQUIPO Y ASIGNACIONES",sectionStyle);
            GUILayout.Label($"3 drones · {m.speed} m/s · sensor {m.sensorRadius} m",textStyle);
            for (int d = 0; d < 3; d++)
            {
                int found = simulation.Findings.FindAll(f => f.drone == d).Count;
                GUILayout.Label($"D{d+1} · ({simulation.Drones[d].x:F1}, {simulation.Drones[d].y:F1}) m · {found} hallazgos",textStyle);
            }
            GUILayout.Space(10);
            GUILayout.Label("FINALIZACIÓN",sectionStyle);
            GUILayout.Label($"Éxito al encontrar {m.requiredFindings} objetos. También termina por rutas agotadas, límite temporal o interrupción.",smallStyle);
            GUILayout.Space(16);
            GUILayout.Label("PROPUESTA DE MISIÓN",sectionStyle);
            if (proposedMission == null)
                GUILayout.Label("Sin propuesta interpretada. Puedes revisar una misión manual desde los detalles JSON.",smallStyle);
            else
            {
                GUILayout.Label(proposedIntent == null ? $"Origen: JSON manual.\nBuscar {proposedMission.requiredFindings} objetos en {proposedMission.width} × {proposedMission.height} m. Límite: {proposedMission.maxSeconds} s." : proposedIntent.Summary(),textStyle);
                GUILayout.Label("Fin: objetivo alcanzado, rutas agotadas, límite temporal o interrupción.",smallStyle);
            }
            GUI.enabled = !running && !interpreting && proposedMission != null;
            if (GUILayout.Button("Validar y preparar",buttonStyle,GUILayout.Height(40)))
                Attempt(PrepareProposal);
            GUI.enabled = true;
            GUILayout.Label("Preparar sustituye la misión activa. Iniciar permanece bajo el mapa.",smallStyle);
            GUILayout.Space(12);
            if (GUILayout.Button((showJson ? "▼" : "▶") + " Detalles técnicos · JSON",buttonStyle,GUILayout.Height(36))) showJson = !showJson;
            GUI.enabled = !interpreting;
            if (showJson) DrawJsonDetails();
            GUI.enabled = true;
            if (GUILayout.Button("Exportar registro",buttonStyle,GUILayout.Height(32))) Export();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(panel.x+16,panel.yMax-44,panel.width-32,40));
            GUILayout.Label(new GUIContent(status,status),smallStyle);
            GUILayout.EndArea();
        }
        void DrawJsonDetails()
        {
            GUILayout.Label("Acepta una respuesta estructurada del intérprete o una misión manual con rutas.",smallStyle);
            jsonScroll = GUILayout.BeginScrollView(jsonScroll,GUILayout.Height(140));
            string edited = GUILayout.TextArea(jsonDraft,jsonStyle,GUILayout.ExpandHeight(true));
            if (edited != jsonDraft) { jsonDraft = edited; proposedMission = null; proposedIntent=null; proposalInstruction=""; proposalEvidence=""; interpretationMessage=""; }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Revisar propuesta JSON",buttonStyle,GUILayout.Height(36)))
                Attempt(() => {
                    proposedMission = null;
                    proposedIntent=null; proposalInstruction=""; proposalEvidence="";
                    if (Newtonsoft.Json.Linq.JObject.Parse(jsonDraft).ContainsKey("status"))
                    {
                        var response=Interpretation.Parse(jsonDraft);
                        interpretationMessage=response.message;
                        if (response.status != "proposal") throw new ArgumentException("Esta respuesta no contiene una propuesta ejecutable.");
                        proposedMission=MissionPlanner.Plan(Scenario.From(simulation.Mission),response.mission);
                        proposedIntent=response.mission;
                    }
                    else proposedMission = MissionJson.Parse(jsonDraft);
                    status = "Propuesta manual revisada; pendiente de preparar.";
                });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cargar borrador",buttonStyle,GUILayout.Height(38)))
                Attempt(() => { jsonDraft = File.ReadAllText(missionPath); proposedMission = null; proposedIntent=null; status = "Borrador cargado; pendiente de revisión."; });
            if (GUILayout.Button("Guardar activa",buttonStyle,GUILayout.Height(38)))
                Attempt(() => { File.WriteAllText(missionPath,JsonUtility.ToJson(simulation.Mission,true)); status = "Misión activa guardada: " + missionPath; });
            GUILayout.EndHorizontal();
            GUILayout.Label("Semilla del escenario de ejemplo",smallStyle);
            GUILayout.BeginHorizontal();
            seed = GUILayout.TextField(seed,jsonStyle,GUILayout.Width(70),GUILayout.Height(36));
            if (GUILayout.Button("Proponer barrido",buttonStyle,GUILayout.Height(36)))
                Attempt(() => { proposedIntent=null; proposedMission = Mission.Sweep(int.Parse(seed)); jsonDraft = JsonUtility.ToJson(proposedMission,true); status = "Barrido propuesto; pendiente de preparar."; });
            GUILayout.EndHorizontal();
        }
        void DrawExperimentMap(Rect viewport)
        {
            var o=experimentObservation;if(o==null)return;
            float scale=Mathf.Min(viewport.width/o.width,viewport.height/o.height);
            var area=new Rect(viewport.center.x-o.width*scale/2,viewport.center.y-o.height*scale/2,o.width*scale,o.height*scale);
            Box(new Rect(area.x-2,area.y-2,area.width+4,area.height+4),Color.gray);Box(area,new Color(.09f,.12f,.17f));
            float cellWidth=area.width/o.columns,cellHeight=area.height/o.rows;
            for(int r=0;r<o.completed.Length;r++)
            {
                float coverage=(float)o.regionCoverage[r];
                if(coverage<=0)continue;int x=r%o.columns,y=r/o.columns;
                Box(new Rect(area.x+x*cellWidth,area.yMax-(y+1)*cellHeight,cellWidth,cellHeight),new Color(.12f,.32f,.3f,Mathf.Clamp01(.2f+.8f*coverage)));
            }
            for(int d=0;d<3;d++){DrawPoint(area,o.positions[d],scale,12,colors[d]);var p=o.positions[d];GUI.Label(new Rect(area.x+p.x*scale+8,area.yMax-p.y*scale-10,40,20),"D"+(d+1));}
        }
        void DrawMap(Rect viewport)
        {
            float scale = Mathf.Min(viewport.width/simulation.Mission.width,viewport.height/simulation.Mission.height);
            var area = new Rect(viewport.center.x-simulation.Mission.width*scale/2,viewport.center.y-simulation.Mission.height*scale/2,simulation.Mission.width*scale,simulation.Mission.height*scale);
            Box(new Rect(area.x-2,area.y-2,area.width+4,area.height+4),Color.gray);
            Box(area,new Color(.09f,.12f,.17f));
            for (int i = 0; i < simulation.Covered.Length; i++)
                if (simulation.Covered[i])
                {
                    float x = i%simulation.Columns*simulation.Mission.cellSize, y = i/simulation.Columns*simulation.Mission.cellSize;
                    float w = Mathf.Min(simulation.Mission.cellSize,simulation.Mission.width-x), h = Mathf.Min(simulation.Mission.cellSize,simulation.Mission.height-y);
                    Box(new Rect(area.x+x*scale,area.yMax-(y+h)*scale,w*scale,h*scale),new Color(.12f,.32f,.3f));
                }
            for (int d = 0; d < 3; d++)
            {
                var route = simulation.ActiveRoutes[d].waypoints;
                for (int j = 1; j < route.Length; j++) DrawLine(area,route[j-1],route[j],scale,colors[d]*.4f);
                foreach (var p in simulation.ActiveRoutes[d].waypoints) DrawPoint(area,p,scale,3,colors[d]*.65f);
                DrawPoint(area,simulation.Drones[d],scale,12,colors[d]);
                var pos = simulation.Drones[d];
                GUI.Label(new Rect(area.x+pos.x*scale+8,area.yMax-pos.y*scale-10,40,20),"D"+(d+1));
            }
            foreach (var finding in simulation.Findings) DrawPoint(area,finding.position,scale,7,Color.white);
        }
        static void DrawPoint(Rect area, Point p, float scale, float size, Color color) =>
            Box(new Rect(area.x+p.x*scale-size/2,area.yMax-p.y*scale-size/2,size,size),color);
        static void DrawLine(Rect area, Point a, Point b, float scale, Color color)
        {
            Vector2 start = new Vector2(area.x+a.x*scale,area.yMax-a.y*scale);
            Vector2 end = new Vector2(area.x+b.x*scale,area.yMax-b.y*scale);
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y-start.y,end.x-start.x)*Mathf.Rad2Deg,start);
            Box(new Rect(start.x,start.y,(end-start).magnitude,1),color);
            GUI.matrix = matrix;
        }
    }
}
