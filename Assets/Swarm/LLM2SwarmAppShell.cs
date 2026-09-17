using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLM2Swarm
{
    // Presentation-only selector. Each child keeps its own backend and lifecycle.
    public sealed class LLM2SwarmAppShell : MonoBehaviour
    {
        SimulationRunner fast2D;
        Px4MissionControl missionControl;
        bool showFast2D=true;
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
            fast2D.enabled=true;
            missionControl.enabled=false;
        }

        void EnsureStyles()
        {
            if(title!=null)return;
            title=new GUIStyle(GUI.skin.label){fontSize=21,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
            subtitle=new GUIStyle(GUI.skin.label){fontSize=12,alignment=TextAnchor.MiddleLeft};
            tab=new GUIStyle(GUI.skin.button){fontSize=14,fontStyle=FontStyle.Bold};
            activeTab=new GUIStyle(tab){normal={background=Texture2D.whiteTexture,textColor=new Color(.04f,.07f,.11f)}};
        }

        void Show(bool fast)
        {
            showFast2D=fast;
            fast2D.enabled=fast;
            missionControl.enabled=!fast;
        }

        void OnGUI()
        {
            EnsureStyles();GUI.depth=-100;
            float scale=Mathf.Min(1,Mathf.Min(Screen.width/1060f,Screen.height/700f));
            Matrix4x4 original=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float width=Screen.width/scale;
            GUI.color=new Color(.025f,.04f,.07f);GUI.DrawTexture(new Rect(0,0,width,72),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.Label(new Rect(22,8,310,30),"LLM2Swarm",title);
            GUI.Label(new Rect(22,37,360,22),"Preparación y visualización · backends independientes",subtitle);
            float tabWidth=190;
            if(GUI.Button(new Rect(width-2*tabWidth-30,16,tabWidth,38),"Fast 2D / Lab",showFast2D?activeTab:tab))Show(true);
            if(GUI.Button(new Rect(width-tabWidth-18,16,tabWidth,38),"PX4 / Mission Control",showFast2D?tab:activeTab))Show(false);
            GUI.matrix=original;
        }
    }
}
