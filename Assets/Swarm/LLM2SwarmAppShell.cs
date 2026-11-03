using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLM2Swarm
{
    // Presentation-only selector. Each child keeps its own backend and lifecycle.
    public sealed class LLM2SwarmAppShell : MonoBehaviour
    {
        SimulationRunner fast2D;
        Px4MissionControl missionControl;
        SimulationRunner integrated;
        int selectedTab;
        GUIStyle title,tab,activeTab,subtitle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if(SceneManager.GetActiveScene().name=="SampleScene"&&FindAnyObjectByType<LLM2SwarmAppShell>()==null)
                new GameObject("LLM2Swarm application shell").AddComponent<LLM2SwarmAppShell>();
        }

        void Awake()
        {
            fast2D=new GameObject("Fast 2D Lab").AddComponent<SimulationRunner>();
            missionControl=new GameObject("PX4 Mission Control (read only)").AddComponent<Px4MissionControl>();
            integrated=new GameObject("Integrated mission").AddComponent<SimulationRunner>();
            integrated.ConfigureIntegratedWorkflow();
            fast2D.enabled=true;
            missionControl.enabled=false;
            integrated.enabled=false;
        }

        void EnsureStyles()
        {
            if(title!=null)return;
            title=new GUIStyle(GUI.skin.label){fontSize=21,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
            subtitle=new GUIStyle(GUI.skin.label){fontSize=12,alignment=TextAnchor.MiddleLeft};
            tab=new GUIStyle(GUI.skin.button){fontSize=14,fontStyle=FontStyle.Bold};
            activeTab=new GUIStyle(tab){normal={background=Texture2D.whiteTexture,textColor=new Color(.04f,.07f,.11f)}};
        }

        void Show(int index)
        {
            selectedTab=index;
            fast2D.enabled=index==0;
            missionControl.enabled=index==1 || index==2;
            missionControl.missionEditor=index==2 ? integrated : null;
            integrated.enabled=false;
        }

        void OnGUI()
        {
            EnsureStyles();GUI.depth=-100;
            float scale=Mathf.Min(1,Mathf.Min(Screen.width/1060f,Screen.height/700f));
            Matrix4x4 original=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float width=Screen.width/scale;
            GUI.color=new Color(.025f,.04f,.07f);GUI.DrawTexture(new Rect(0,0,width,72),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.Label(new Rect(22,8,310,30),"LLM2Swarm",title);
            GUI.Label(new Rect(22,37,360,22),selectedTab==2 ? "Producto · instrucción → misión → resultados" : "Laboratorios de experimentación",subtitle);
            float tabWidth=190;
            string[] labels={"Fast 2D / Lab","PX4 / Mission Control","Misión integrada"};
            for(int i=0;i<labels.Length;i++)
                if(GUI.Button(new Rect(width-(3-i)*(tabWidth+8)-10,16,tabWidth,38),labels[i],selectedTab==i?activeTab:tab))Show(i);
            GUI.matrix=original;
        }
    }
}
