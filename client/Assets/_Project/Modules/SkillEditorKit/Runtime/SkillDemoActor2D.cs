namespace SkillEditorKit
{
using UnityEngine;

/// <summary>Small runnable adapter example; replace with the game's movement/health system.</summary>
public sealed class SkillDemoActor2D : MonoBehaviour, ISkillActor, ISkillTeam
{
    public bool PlayerControlled;
    public int Team = 2;
    public float Health = 100, BaseAttack = 10, MoveSpeed = 4;
    public SkillClip Shoot, Melee;
    public Transform ModelTransform => transform;
    public int SkillTeamId => Team;
    private SkillPlayer player;
    private SkillFacing2D facing;
    private SpriteRenderer sprite;
    private Color originalColor;
    private float flashUntil, nextShot;
    public int ReceivedHits { get; private set; }

    private void Start()
    {
        sprite=GetComponentInChildren<SpriteRenderer>();
        if(sprite!=null) originalColor=sprite.color;
        if(!PlayerControlled) return;
        facing=GetComponent<SkillFacing2D>();
        player=GetComponent<SkillPlayer>();
        var animation=GetComponent<SkillAnimationPlayer>(); animation.Init();
        player.Init(this,animation,transform); player.StartPlaySkillBehaviour(new SkillBehaviourBase());
    }
    private void Update()
    {
        if(sprite!=null) sprite.color=Time.time<flashUntil ? Color.white : originalColor;
        if(!PlayerControlled || player==null) return;
        float move=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                   (Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        if(move!=0) { facing.SetFacing(move<0 ? -1 : 1); transform.position+=Vector3.right*(move*MoveSpeed*Time.deltaTime); }
        if(Input.GetKey(KeyCode.J) && Time.time>=nextShot && Shoot!=null)
        { nextShot=Time.time+.3f; player.PlaySkillClip(Shoot); }
        if(Input.GetKeyDown(KeyCode.K) && Melee!=null) player.PlaySkillClip(Melee);
    }
    public float GetAttackValue(SkillAttackDetectionEvent data) => BaseAttack*(data.AttackHitConfig?.AttackMultiply ?? 1);
    public void BeHit(SkillHitData data)
    { ReceivedHits++; Health=Mathf.Max(0,Health-data.attackValue); flashUntil=Time.time+.12f; }
    private void OnGUI()
    {
        if(!PlayerControlled) return;
        GUI.Box(new Rect(16,16,600,75),"2D Skill Demo — A/D or arrows: move / turn\nHold J: shoot     K: melee\nTargets flash when hit. Edit Shoot2D / Melee2D in the skill workbench.");
    }
}
}
