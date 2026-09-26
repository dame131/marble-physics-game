using System;
using System.Collections.Generic;
using UnityEngine;

// A single-scene playable Unity foundation. Attached marbles use no Rigidbodies;
// falling marbles gain physics only after their connection to the ceiling is cut.
public sealed class MarblePiece : MonoBehaviour
{
    public int ColorIndex, Row = -1, Column = -1;
    public bool IsFalling, Scored;
    public Rigidbody Body;
}

public sealed class MarbleAvalancheGame : MonoBehaviour
{
    const int Columns = 21, Rows = 30, PaletteCount = 8;
    const float Radius = .185f, StepX = .37f, StepY = .325f;
    static readonly Color[] Palette = {
        new Color(1f,.09f,.27f), new Color(.02f,.86f,.98f), new Color(1f,.72f,.06f),
        new Color(.57f,.19f,.97f), new Color(.21f,.94f,.31f), new Color(1f,.35f,.05f),
        new Color(.05f,.34f,1f), new Color(1f,.12f,.65f)
    };
    readonly MarblePiece[,] cells = new MarblePiece[Rows, Columns];
    readonly List<MarblePiece> falling = new List<MarblePiece>();
    readonly Material[] materials = new Material[PaletteCount];
    readonly Collider[] basketTriggers = new Collider[3];
    readonly int[] basketValues = {100,250,100};
    Camera cam;
    Transform barrel;
    ParticleSystem smoke;
    MarblePiece projectile;
    Vector3 velocity;
    Vector3 direction = Vector3.up;
    int level = 1, unlocked = 1, score, shots, currentColor, nextColor, dropCount, goal, powers;
    bool playing, aiming, settling, won, showingMap = true;
    float settleUntil, recoil, bannerUntil;
    string banner = "";
    Texture2D portrait;
    Material portraitMaterial;
    readonly HashSet<int> revealed = new HashSet<int>();
    readonly List<GameObject> revealTiles = new List<GameObject>();
    GameObject fullPortrait;
    Vector2 touchStart, mapScroll;
    bool ready;
    System.Random random;

    void Awake()
    {
        random = new System.Random(Environment.TickCount);
        unlocked = Mathf.Clamp(PlayerPrefs.GetInt("UnlockedLevel",1),1,100);
        score = PlayerPrefs.GetInt("TotalScore",0);
        cam = Camera.main;
        if (!cam) { var cameraObject = new GameObject("Main Camera"); cam = cameraObject.AddComponent<Camera>(); cameraObject.tag = "MainCamera"; }
        cam.orthographic = true; cam.orthographicSize = 9.1f; cam.backgroundColor = new Color(.035f,.36f,.56f);
        cam.transform.position = new Vector3(0,0,-20); cam.transform.rotation = Quaternion.identity;
        RenderSettings.ambientLight = new Color(.8f,.86f,1f);
        var lightObject = new GameObject("Marble highlights"); var lamp = lightObject.AddComponent<Light>();
        lamp.type = LightType.Directional; lamp.intensity = 1.8f; lamp.transform.rotation = Quaternion.Euler(35,-35,0);
        for (int i=0;i<PaletteCount;i++) {
            var m = new Material(Shader.Find("Standard")); m.color = Palette[i]; m.SetFloat("_Metallic",.22f);
            m.SetFloat("_Glossiness",.93f); materials[i] = m;
        }
        MakeScenery(); MakeCannon(); MakeBaskets(); LoadPortrait();
        ready = true;
    }

    void MakeScenery()
    {
        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad); backdrop.name = "Ocean background";
        backdrop.transform.position = new Vector3(0,0,5);
        backdrop.transform.localScale = new Vector3(17,21,1);
        Destroy(backdrop.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Unlit/Color")); mat.color = new Color(.035f,.48f,.69f);
        backdrop.GetComponent<Renderer>().material = mat;
        for(int i=0;i<22;i++) {
            var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bubble.name = "Background bubble"; float x = (i*37%137)/10f-6.8f, y=(i*53%181)/10f-9f;
            bubble.transform.position = new Vector3(x,y,3.8f);
            float s=.06f+(i%4)*.065f; bubble.transform.localScale = Vector3.one*s;
            Destroy(bubble.GetComponent<Collider>());
            var bubbleMat = new Material(Shader.Find("Standard")); bubbleMat.color = new Color(.57f,.94f,1f,.36f);
            bubbleMat.SetFloat("_Glossiness",1); bubble.GetComponent<Renderer>().material = bubbleMat;
        }
    }

    void MakeCannon()
    {
        var pivot = new GameObject("Swivel cannon"); pivot.transform.position = new Vector3(0,-6.45f,-1);
        barrel = pivot.transform;
        var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder); tube.name = "Cannon barrel";
        tube.transform.SetParent(barrel,false); tube.transform.localPosition = new Vector3(0,.31f,0);
        tube.transform.localScale = new Vector3(.32f,.48f,.32f); Destroy(tube.GetComponent<Collider>());
        var metal = new Material(Shader.Find("Standard")); metal.color = new Color(.62f,.11f,.10f);
        metal.SetFloat("_Metallic",.85f); metal.SetFloat("_Glossiness",.84f); tube.GetComponent<Renderer>().material = metal;
        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder); rim.name="Gold muzzle";
        rim.transform.SetParent(barrel,false); rim.transform.localPosition=new Vector3(0,.82f,0);
        rim.transform.localScale=new Vector3(.39f,.10f,.39f); Destroy(rim.GetComponent<Collider>());
        var gold = new Material(Shader.Find("Standard")); gold.color = new Color(1,.67f,.11f);
        gold.SetFloat("_Metallic",.9f); gold.SetFloat("_Glossiness",.9f); rim.GetComponent<Renderer>().material=gold;
        var wheel = GameObject.CreatePrimitive(PrimitiveType.Sphere); wheel.name="Cannon pivot";
        wheel.transform.position=barrel.position+Vector3.back*.12f; wheel.transform.localScale=Vector3.one*.64f;
        Destroy(wheel.GetComponent<Collider>()); wheel.GetComponent<Renderer>().material=gold;
        var smokeObject = new GameObject("Cannon muzzle smoke"); smokeObject.transform.SetParent(barrel,false);
        smokeObject.transform.localPosition = new Vector3(0,.88f,-.08f); smoke=smokeObject.AddComponent<ParticleSystem>();
        var main=smoke.main; main.playOnAwake=false; main.startLifetime=.65f; main.startSpeed=1.6f;
        main.startSize=.19f; main.startColor=new Color(.94f,.95f,.98f,.7f); main.maxParticles=80;
        var emission=smoke.emission; emission.enabled=false;
        var shape=smoke.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=24;
        smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void MakeBaskets()
    {
        for(int i=0;i<3;i++) {
            float x=(i-1)*2.65f;
            var basket = GameObject.CreatePrimitive(PrimitiveType.Cube); basket.name="Gold basket "+basketValues[i];
            basket.transform.position=new Vector3(x,-8.12f,.1f);
            basket.transform.localScale=new Vector3(1.55f,.38f,.55f);
            var mat=new Material(Shader.Find("Standard")); mat.color=new Color(1,.65f,.08f);
            mat.SetFloat("_Metallic",.9f); mat.SetFloat("_Glossiness",.7f);
            basket.GetComponent<Renderer>().material=mat;
            var collider=basket.GetComponent<BoxCollider>(); collider.isTrigger=true;
            collider.size=new Vector3(1,2.5f,2); basketTriggers[i]=collider;
        }
    }

    void LoadPortrait()
    {
        var source=Resources.Load<Texture2D>("Portrait");
        if(!source) return;
        // Photo is intentionally a private, local asset. Rotate its UV on each tile,
        // so the source JPEG remains unchanged and both people stand upright.
        portrait=source;
        portraitMaterial=new Material(Shader.Find("Unlit/Texture")); portraitMaterial.mainTexture=portrait;
        fullPortrait=GameObject.CreatePrimitive(PrimitiveType.Quad); fullPortrait.name="Full photo after clearing the top";
        Destroy(fullPortrait.GetComponent<Collider>());
        fullPortrait.transform.position=new Vector3(0,2.5f,1.45f);
        fullPortrait.transform.localScale=new Vector3(7.6f,11.3f,1);
        fullPortrait.GetComponent<Renderer>().material=portraitMaterial;
        var mesh=fullPortrait.GetComponent<MeshFilter>().mesh;
        mesh.uv=new [] { new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0) };
        fullPortrait.SetActive(false);
    }

    Vector3 Position(int r,int c) => new Vector3((c-10)*StepX+(r%2)*Radius,7.58f-r*StepY,0);
    int ColorRoll() => random.Next(Math.Min(8,6+level));

    void BeginLevel(int selected)
    {
        level=selected; score=Math.Max(0,score); won=false; playing=true; showingMap=false;
        settling=false; projectile=null; revealed.Clear();
        foreach(var tile in revealTiles) if(tile) Destroy(tile); revealTiles.Clear();
        if(fullPortrait) fullPortrait.SetActive(false);
        for(int r=0;r<Rows;r++) for(int c=0;c<Columns;c++) {
            if(cells[r,c]) Destroy(cells[r,c].gameObject); cells[r,c]=null;
        }
        foreach(var m in falling) if(m) Destroy(m.gameObject); falling.Clear();
        shots=level==1?35:level==2?42:48; goal=level==1?30:level==2?45:55;
        powers=2; dropCount=0; currentColor=ColorRoll(); nextColor=ColorRoll();
        int height=level==1?23:level==2?27:Math.Min(30,24+level%7);
        for(int r=0;r<height;r++) for(int c=0;c<Columns;c++) {
            bool inside=r<3||Math.Abs(c-10)<10.5f-r*.08f;
            if(inside && random.NextDouble() > (r>12?.16:.05)) {
                var marble=CreateMarble(ColorRoll(),Position(r,c));
                marble.Row=r; marble.Column=c; cells[r,c]=marble;
            }
        }
    }

    MarblePiece CreateMarble(int color,Vector3 pos)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere); obj.name="Marble "+color;
        obj.transform.position=pos; obj.transform.localScale=Vector3.one*(Radius*2);
        obj.GetComponent<Renderer>().sharedMaterial=materials[color];
        var marble=obj.AddComponent<MarblePiece>(); marble.ColorIndex=color;
        return marble;
    }

    IEnumerable<Vector2Int> Adjacent(int r,int c)
    {
        for(int dr=-1;dr<=1;dr++) for(int dc=-1;dc<=1;dc++) {
            int nr=r+dr,nc=c+dc;
            if(nr<0||nr>=Rows||nc<0||nc>=Columns||nr==r&&nc==c) continue;
            if(Vector3.Distance(Position(r,c),Position(nr,nc))<Radius*2.15f)
                yield return new Vector2Int(nr,nc);
        }
    }

    void Update()
    {
        if(!ready) return;
        if(playing&&!won) HandleInput();
        if(projectile) MoveProjectile();
        for(int i=falling.Count-1;i>=0;i--) {
            var m=falling[i]; if(!m) {falling.RemoveAt(i);continue;}
            if(m.Scored||m.transform.position.y<-9.2f) {Destroy(m.gameObject);falling.RemoveAt(i);continue;}
            for(int b=0;b<3;b++) if(basketTriggers[b].bounds.Contains(m.transform.position)) {
                m.Scored=true; score+=basketValues[b]; banner="+"+basketValues[b]; bannerUntil=Time.time+.7f; break;
            }
        }
        if(recoil>0) {recoil=Mathf.MoveTowards(recoil,0,Time.deltaTime*.85f);barrel.localPosition=new Vector3(0,-recoil,0);}
        if(settling && !projectile && Time.time>=settleUntil) {
            settling=false; int left=0;bool topEmpty=true;
            for(int r=0;r<Rows;r++) for(int c=0;c<Columns;c++) {
                if(cells[r,c]) {left++; if(r==0) topEmpty=false;}
            }
            if(left<5||dropCount>=goal&&topEmpty) Win();
            else if(shots<=0) {playing=false; banner="OUT OF SHOTS";}
        }
    }

    void HandleInput()
    {
        if(Input.touchCount>0) {
            var touch=Input.GetTouch(0);
            if(touch.phase==TouchPhase.Began) {touchStart=touch.position; aiming=playing&&touch.position.y>Screen.height*.09f&&touch.position.y<Screen.height*.9f;}
            if(aiming&&(touch.phase==TouchPhase.Moved||touch.phase==TouchPhase.Stationary)) Aim(touch.position);
            if(aiming&&(touch.phase==TouchPhase.Ended||touch.phase==TouchPhase.Canceled)) {Aim(touch.position);aiming=false;Fire();}
        } else {
            if(Input.GetMouseButtonDown(0)) {touchStart=Input.mousePosition;aiming=playing&&touchStart.y>Screen.height*.09f&&touchStart.y<Screen.height*.9f;}
            if(aiming&&Input.GetMouseButton(0)) Aim(Input.mousePosition);
            if(aiming&&Input.GetMouseButtonUp(0)) {Aim(Input.mousePosition);aiming=false;Fire();}
        }
    }

    void Aim(Vector2 screen)
    {
        if(screen.y<Screen.height*.09f||screen.y>Screen.height*.9f) return;
        Vector3 world=cam.ScreenToWorldPoint(new Vector3(screen.x,screen.y,20));
        var candidate=(world-barrel.position); candidate.z=0;
        float angle=Mathf.Clamp(Mathf.Atan2(candidate.y,candidate.x)*Mathf.Rad2Deg,8,172);
        direction=new Vector3(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad),0);
        barrel.rotation=Quaternion.FromToRotation(Vector3.up,direction);
    }

    void Fire()
    {
        if(!playing||projectile||settling||shots<=0)return;
        shots--; projectile=CreateMarble(currentColor,barrel.position+direction*.98f);
        velocity=direction*12f; recoil=.23f;
        smoke.Emit(24);
        currentColor=nextColor;nextColor=ColorRoll();
    }

    void MoveProjectile()
    {
        var position=projectile.transform.position+velocity*Time.deltaTime;
        float limit=Mathf.Min(3.94f,cam.orthographicSize*cam.aspect-Radius);
        if(position.x<-limit||position.x>limit) {
            position.x=Mathf.Clamp(position.x,-limit,limit);velocity.x=-velocity.x;
        }
        projectile.transform.position=position;
        bool hit=position.y>=7.55f;
        for(int r=0;r<Rows&&!hit;r++) for(int c=0;c<Columns;c++)
            if(cells[r,c]&&Vector3.Distance(position,Position(r,c))<Radius*1.9f) {hit=true;break;}
        if(hit) AttachProjectile();
    }

    void AttachProjectile()
    {
        if(!projectile)return;
        Vector3 position=projectile.transform.position;
        int br=-1,bc=-1;float best=float.MaxValue;
        for(int r=0;r<Rows;r++) for(int c=0;c<Columns;c++) if(!cells[r,c]) {
            Vector3 p=Position(r,c);float d=(p-position).sqrMagnitude;
            if(d<best) {best=d;br=r;bc=c;}
        }
        if(br<0) {Destroy(projectile.gameObject);projectile=null;return;}
        projectile.Row=br;projectile.Column=bc;projectile.transform.position=Position(br,bc);
        cells[br,bc]=projectile; int color=projectile.ColorIndex;projectile=null;
        var group=SameColor(br,bc,color);
        if(group.Count>=3) {
            foreach(var p in group) RemoveCell(p.x,p.y);
            score+=group.Count*30;DropUnsupported();
        }
        settling=true;settleUntil=Time.time+1.15f;
    }

    List<Vector2Int> SameColor(int r,int c,int color)
    {
        var found=new List<Vector2Int>();var queue=new Queue<Vector2Int>();var visited=new HashSet<int>();
        queue.Enqueue(new Vector2Int(r,c));visited.Add(r*Columns+c);
        while(queue.Count>0) {
            var p=queue.Dequeue();found.Add(p);
            foreach(var n in Adjacent(p.x,p.y)) {
                int id=n.x*Columns+n.y;
                if(visited.Add(id)&&cells[n.x,n.y]&&cells[n.x,n.y].ColorIndex==color)queue.Enqueue(n);
            }
        }
        return found;
    }

    void RemoveCell(int r,int c)
    {
        var marble=cells[r,c];if(!marble)return;
        cells[r,c]=null; Reveal(r,c); Destroy(marble.gameObject);
    }

    void DropUnsupported()
    {
        var connected=new HashSet<int>();var queue=new Queue<Vector2Int>();
        for(int c=0;c<Columns;c++)if(cells[0,c]) {queue.Enqueue(new Vector2Int(0,c));connected.Add(c);}
        while(queue.Count>0) {
            var p=queue.Dequeue();foreach(var n in Adjacent(p.x,p.y)) {
                int id=n.x*Columns+n.y;
                if(cells[n.x,n.y]&&connected.Add(id))queue.Enqueue(n);
            }
        }
        for(int r=0;r<Rows;r++) for(int c=0;c<Columns;c++) {
            var marble=cells[r,c];if(!marble||connected.Contains(r*Columns+c))continue;
            cells[r,c]=null;Reveal(r,c);dropCount++;
            marble.IsFalling=true;marble.Row=-1;
            marble.Body=marble.gameObject.AddComponent<Rigidbody>();
            marble.Body.mass=.4f;marble.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            marble.Body.AddForce(new Vector3(((float)random.NextDouble()-.5f)*1.2f,1,0),ForceMode.Impulse);
            falling.Add(marble);score+=20;
        }
    }

    void Reveal(int r,int c)
    {
        if(!portraitMaterial||!revealed.Add(r*Columns+c)) return;
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name="Revealed photo tile";Destroy(quad.GetComponent<Collider>());
        quad.transform.position=Position(r,c)+new Vector3(0,0,1.4f);
        quad.transform.localScale=Vector3.one*(Radius*2.03f);
        var mesh=quad.GetComponent<MeshFilter>().mesh;
        // Source landscape JPEG is shown upright by a 90-degree UV rotation.
        var p=Position(r,c);
        float u=(p.x+3.8f)/7.6f,v=(p.y+3.15f)/11.3f;
        float du=Radius/7.6f,dv=Radius/11.3f;
        mesh.uv=new [] {
            new Vector2(v-dv,1-(u-du)),new Vector2(v+dv,1-(u-du)),
            new Vector2(v-dv,1-(u+du)),new Vector2(v+dv,1-(u+du))
        };
        quad.GetComponent<Renderer>().sharedMaterial=portraitMaterial;
        revealTiles.Add(quad);
    }

    void Blast()
    {
        if(!playing||settling||projectile||powers<=0)return;
        powers--;int cleared=0;
        for(int r=0;r<Rows;r++) for(int c=0;c<Columns;c++) {
            if(!cells[r,c])continue;
            var p=Position(r,c);
            if(p.y>-.2f&&p.y<3f&&Mathf.Abs(p.x)<1.35f&&cleared<22) {
                RemoveCell(r,c);cleared++;score+=30;
            }
        }
        DropUnsupported();settling=true;settleUntil=Time.time+1.2f;
    }

    void Win()
    {
        won=true;playing=false;score+=shots*25;
        unlocked=Mathf.Max(unlocked,Mathf.Min(100,level+1));
        PlayerPrefs.SetInt("UnlockedLevel",unlocked);
        PlayerPrefs.SetInt("TotalScore",score);PlayerPrefs.Save();
        if(fullPortrait) fullPortrait.SetActive(true);
        banner="PHOTO REVEALED!";bannerUntil=Time.time+3;
    }

    void OnGUI()
    {
        if(!ready)return;
        float unit=Mathf.Min(Screen.width/412f,Screen.height/915f);
        var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(unit,unit,1));
        float width=Screen.width/unit,height=Screen.height/unit;
        var title=new GUIStyle(GUI.skin.label) {fontSize=21,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        title.normal.textColor=Color.white;
        GUI.Box(new Rect(2,0,width-4,66),"");
        GUI.Label(new Rect(0,5,width/4,48),"LEVEL\n"+level,title);
        GUI.Label(new Rect(width/4,5,width/4,48),"SCORE\n"+score,title);
        GUI.Label(new Rect(width/2,5,width/4,48),"SHOTS\n"+shots,title);
        GUI.Label(new Rect(width*3/4,5,width/4,48),"DROP\n"+dropCount+"/"+goal,title);
        var btn=new GUIStyle(GUI.skin.button) {fontSize=19,fontStyle=FontStyle.Bold};
        float y=height-72f;
        if(GUI.Button(new Rect(6,y,(width-30)/4,59),"MAP",btn)) {showingMap=true;playing=false;}
        if(GUI.Button(new Rect(12+(width-30)/4,y,(width-30)/4,59),"SWAP",btn))
            if(playing&&!projectile) {int temp=currentColor;currentColor=nextColor;nextColor=temp;}
        if(GUI.Button(new Rect(18+2*(width-30)/4,y,(width-30)/4,59),"BLAST "+powers,btn)) Blast();
        if(GUI.Button(new Rect(24+3*(width-30)/4,y,(width-30)/4,59),"PAUSE",btn))playing=!playing;
        if(showingMap) {
            GUI.Box(new Rect(12,74,width-24,height-155),"MARBLE TRAIL");
            var scroll=new GUIStyle(GUI.skin.label) {fontSize=15,alignment=TextAnchor.MiddleCenter};
            GUI.Label(new Rect(20,95,width-40,35),"100 stops · completed levels unlock the next",scroll);
            var area=new Rect(20,135,width-40,height-225);
            mapScroll=GUI.BeginScrollView(area,mapScroll,new Rect(0,0,area.width-20,25*62));
            for(int i=1;i<=100;i++) {
                int row=(i-1)/4,column=(i-1)%4;
                if(row%2==1) column=3-column;
                var rect=new Rect(column*(area.width/4)+4,row*62,area.width/4-8,53);
                bool enabled=GUI.enabled;GUI.enabled=i<=unlocked;
                if(GUI.Button(rect,i<=unlocked?i+" ★":"🔒",btn))BeginLevel(i);
                GUI.enabled=enabled;
            }
            GUI.EndScrollView();
        } else if(won||!playing&&shots<=0) {
            GUI.Box(new Rect(42,height*.39f,width-84,180),won?"LEVEL CLEAR — PHOTO REVEALED":"OUT OF SHOTS");
            if(GUI.Button(new Rect(72,height*.39f+102,width-144,55),won?"NEXT LEVEL":"RETRY",btn))BeginLevel(won?Math.Min(100,level+1):level);
        }
        if(Time.time<bannerUntil)GUI.Label(new Rect(0,height*.67f,width,48),banner,title);
        GUI.matrix=old;
    }
}
