using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"Cinemachine.dll",
		"DOTween.dll",
		"Google.Protobuf.dll",
		"LitJson.dll",
		"System.Core.dll",
		"System.dll",
		"Unity.TextMeshPro.dll",
		"UnityEngine.AssetBundleModule.dll",
		"UnityEngine.CoreModule.dll",
		"UnityEngine.JSONSerializeModule.dll",
		"UnityEngine.TilemapModule.dll",
		"mscorlib.dll",
	};
	// }}

	// {{ constraint implement type
	// }} 

	// {{ AOT generic types
	// Google.Protobuf.Collections.RepeatedField.<GetEnumerator>d__22<int>
	// Google.Protobuf.Collections.RepeatedField.<GetEnumerator>d__22<object>
	// Google.Protobuf.Collections.RepeatedField<int>
	// Google.Protobuf.Collections.RepeatedField<object>
	// Google.Protobuf.FieldCodec.<>c__16<object>
	// Google.Protobuf.FieldCodec.<>c__DisplayClass16_0<object>
	// Google.Protobuf.FieldCodec.<>c__DisplayClass30_0<int>
	// Google.Protobuf.FieldCodec.<>c__DisplayClass30_0<object>
	// Google.Protobuf.FieldCodec<int>
	// Google.Protobuf.FieldCodec<object>
	// Google.Protobuf.IDeepCloneable<int>
	// Google.Protobuf.IDeepCloneable<object>
	// Google.Protobuf.IMessage<object>
	// Google.Protobuf.MessageParser.<>c__DisplayClass2_0<object>
	// Google.Protobuf.MessageParser<object>
	// System.Action<SkillEditorKit.SkillHitData>
	// System.Action<SkillEditorKit.SkillProjectile.Contact>
	// System.Action<StringSlice>
	// System.Action<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Action<UnityEngine.Animations.AnimationClipPlayable>
	// System.Action<UnityEngine.CombineInstance>
	// System.Action<UnityEngine.EventSystems.RaycastResult>
	// System.Action<UnityEngine.Vector2>
	// System.Action<UnityEngine.Vector2Int>
	// System.Action<UnityEngine.Vector3,UnityEngine.Quaternion>
	// System.Action<UnityEngine.Vector3>
	// System.Action<YouYou.DataTable.DTBaseRole>
	// System.Action<YouYou.DataTable.DTBattleAttr>
	// System.Action<YouYou.DataTable.DTBuff>
	// System.Action<YouYou.DataTable.DTJob>
	// System.Action<YouYou.DataTable.DTJobLevel>
	// System.Action<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Action<YouYou.DataTable.DTRoleAnimCategory>
	// System.Action<YouYou.DataTable.DTRoleAnimation>
	// System.Action<YouYou.DataTable.DTSkill>
	// System.Action<YouYou.DataTable.DTSkillLevel>
	// System.Action<YouYou.DataTable.DTSprite>
	// System.Action<YouYou.DataTable.DTSys_Audio>
	// System.Action<YouYou.DataTable.DTSys_Code>
	// System.Action<YouYou.DataTable.DTSys_Effect>
	// System.Action<YouYou.DataTable.DTSys_Prefab>
	// System.Action<YouYou.DataTable.DTSys_Scene>
	// System.Action<YouYou.DataTable.DTSys_SceneDetail>
	// System.Action<YouYou.DataTable.DTSys_StorySound>
	// System.Action<YouYou.DataTable.DTSys_UIForm>
	// System.Action<double>
	// System.Action<float>
	// System.Action<int,int,int>
	// System.Action<int>
	// System.Action<object,SkillEditorKit.SkillHitData>
	// System.Action<object,int,UnityEngine.Vector3>
	// System.Action<object,int>
	// System.Action<object,object>
	// System.Action<object>
	// System.Action<ushort>
	// System.ArraySegment.Enumerator<byte>
	// System.ArraySegment<byte>
	// System.Collections.Concurrent.ConcurrentQueue.<Enumerate>d__28<object>
	// System.Collections.Concurrent.ConcurrentQueue.Segment<object>
	// System.Collections.Concurrent.ConcurrentQueue<object>
	// System.Collections.Generic.ArraySortHelper<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.ArraySortHelper<StringSlice>
	// System.Collections.Generic.ArraySortHelper<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.CombineInstance>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.Vector2>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.Vector2Int>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.Vector3>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTJob>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.ArraySortHelper<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.ArraySortHelper<float>
	// System.Collections.Generic.ArraySortHelper<int>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.ArraySortHelper<ushort>
	// System.Collections.Generic.Comparer<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.Comparer<StringSlice>
	// System.Collections.Generic.Comparer<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.Comparer<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.Comparer<UnityEngine.CombineInstance>
	// System.Collections.Generic.Comparer<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.Comparer<UnityEngine.Vector2>
	// System.Collections.Generic.Comparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.Comparer<UnityEngine.Vector3>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTJob>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.Comparer<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.Comparer<float>
	// System.Collections.Generic.Comparer<int>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.Comparer<ushort>
	// System.Collections.Generic.Dictionary.Enumerator<BigWorld.Pooling.PoolKey,int>
	// System.Collections.Generic.Dictionary.Enumerator<BigWorld.Pooling.PoolKey,object>
	// System.Collections.Generic.Dictionary.Enumerator<byte,object>
	// System.Collections.Generic.Dictionary.Enumerator<byte,ushort>
	// System.Collections.Generic.Dictionary.Enumerator<int,FMOD.Studio.EventInstance>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.Dictionary.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.Dictionary.Enumerator<int,UnityEngine.Color>
	// System.Collections.Generic.Dictionary.Enumerator<int,byte>
	// System.Collections.Generic.Dictionary.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.Enumerator<long,int>
	// System.Collections.Generic.Dictionary.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.Enumerator<object,FlatBuffers.StringOffset>
	// System.Collections.Generic.Dictionary.Enumerator<object,SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.Dictionary.Enumerator<object,double>
	// System.Collections.Generic.Dictionary.Enumerator<object,float>
	// System.Collections.Generic.Dictionary.Enumerator<object,int>
	// System.Collections.Generic.Dictionary.Enumerator<object,long>
	// System.Collections.Generic.Dictionary.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.Enumerator<object,ulong>
	// System.Collections.Generic.Dictionary.Enumerator<object,ushort>
	// System.Collections.Generic.Dictionary.Enumerator<sbyte,object>
	// System.Collections.Generic.Dictionary.Enumerator<ushort,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<BigWorld.Pooling.PoolKey,int>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<BigWorld.Pooling.PoolKey,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<byte,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<byte,ushort>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,FMOD.Studio.EventInstance>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,UnityEngine.Color>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,byte>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<long,int>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,FlatBuffers.StringOffset>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,double>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,float>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,int>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,long>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,ulong>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,ushort>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<sbyte,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<ushort,object>
	// System.Collections.Generic.Dictionary.KeyCollection<BigWorld.Pooling.PoolKey,int>
	// System.Collections.Generic.Dictionary.KeyCollection<BigWorld.Pooling.PoolKey,object>
	// System.Collections.Generic.Dictionary.KeyCollection<byte,object>
	// System.Collections.Generic.Dictionary.KeyCollection<byte,ushort>
	// System.Collections.Generic.Dictionary.KeyCollection<int,FMOD.Studio.EventInstance>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.Dictionary.KeyCollection<int,UnityEngine.Color>
	// System.Collections.Generic.Dictionary.KeyCollection<int,byte>
	// System.Collections.Generic.Dictionary.KeyCollection<int,float>
	// System.Collections.Generic.Dictionary.KeyCollection<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection<long,int>
	// System.Collections.Generic.Dictionary.KeyCollection<long,object>
	// System.Collections.Generic.Dictionary.KeyCollection<object,FlatBuffers.StringOffset>
	// System.Collections.Generic.Dictionary.KeyCollection<object,SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.Dictionary.KeyCollection<object,double>
	// System.Collections.Generic.Dictionary.KeyCollection<object,float>
	// System.Collections.Generic.Dictionary.KeyCollection<object,int>
	// System.Collections.Generic.Dictionary.KeyCollection<object,long>
	// System.Collections.Generic.Dictionary.KeyCollection<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection<object,ulong>
	// System.Collections.Generic.Dictionary.KeyCollection<object,ushort>
	// System.Collections.Generic.Dictionary.KeyCollection<sbyte,object>
	// System.Collections.Generic.Dictionary.KeyCollection<ushort,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<BigWorld.Pooling.PoolKey,int>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<BigWorld.Pooling.PoolKey,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<byte,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<byte,ushort>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,FMOD.Studio.EventInstance>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,UnityEngine.Color>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,byte>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<long,int>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,FlatBuffers.StringOffset>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,double>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,float>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,int>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,long>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,ulong>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,ushort>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<sbyte,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<ushort,object>
	// System.Collections.Generic.Dictionary.ValueCollection<BigWorld.Pooling.PoolKey,int>
	// System.Collections.Generic.Dictionary.ValueCollection<BigWorld.Pooling.PoolKey,object>
	// System.Collections.Generic.Dictionary.ValueCollection<byte,object>
	// System.Collections.Generic.Dictionary.ValueCollection<byte,ushort>
	// System.Collections.Generic.Dictionary.ValueCollection<int,FMOD.Studio.EventInstance>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.Dictionary.ValueCollection<int,UnityEngine.Color>
	// System.Collections.Generic.Dictionary.ValueCollection<int,byte>
	// System.Collections.Generic.Dictionary.ValueCollection<int,float>
	// System.Collections.Generic.Dictionary.ValueCollection<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection<long,int>
	// System.Collections.Generic.Dictionary.ValueCollection<long,object>
	// System.Collections.Generic.Dictionary.ValueCollection<object,FlatBuffers.StringOffset>
	// System.Collections.Generic.Dictionary.ValueCollection<object,SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.Dictionary.ValueCollection<object,double>
	// System.Collections.Generic.Dictionary.ValueCollection<object,float>
	// System.Collections.Generic.Dictionary.ValueCollection<object,int>
	// System.Collections.Generic.Dictionary.ValueCollection<object,long>
	// System.Collections.Generic.Dictionary.ValueCollection<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection<object,ulong>
	// System.Collections.Generic.Dictionary.ValueCollection<object,ushort>
	// System.Collections.Generic.Dictionary.ValueCollection<sbyte,object>
	// System.Collections.Generic.Dictionary.ValueCollection<ushort,object>
	// System.Collections.Generic.Dictionary<BigWorld.Pooling.PoolKey,int>
	// System.Collections.Generic.Dictionary<BigWorld.Pooling.PoolKey,object>
	// System.Collections.Generic.Dictionary<byte,object>
	// System.Collections.Generic.Dictionary<byte,ushort>
	// System.Collections.Generic.Dictionary<int,FMOD.Studio.EventInstance>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.Dictionary<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.Dictionary<int,UnityEngine.Color>
	// System.Collections.Generic.Dictionary<int,byte>
	// System.Collections.Generic.Dictionary<int,float>
	// System.Collections.Generic.Dictionary<int,object>
	// System.Collections.Generic.Dictionary<long,int>
	// System.Collections.Generic.Dictionary<long,object>
	// System.Collections.Generic.Dictionary<object,FlatBuffers.StringOffset>
	// System.Collections.Generic.Dictionary<object,SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.Dictionary<object,double>
	// System.Collections.Generic.Dictionary<object,float>
	// System.Collections.Generic.Dictionary<object,int>
	// System.Collections.Generic.Dictionary<object,long>
	// System.Collections.Generic.Dictionary<object,object>
	// System.Collections.Generic.Dictionary<object,ulong>
	// System.Collections.Generic.Dictionary<object,ushort>
	// System.Collections.Generic.Dictionary<sbyte,object>
	// System.Collections.Generic.Dictionary<ushort,object>
	// System.Collections.Generic.EqualityComparer<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.EqualityComparer<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.EqualityComparer<FMOD.Studio.EventInstance>
	// System.Collections.Generic.EqualityComparer<FlatBuffers.StringOffset>
	// System.Collections.Generic.EqualityComparer<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.EqualityComparer<System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.EqualityComparer<UnityEngine.Color>
	// System.Collections.Generic.EqualityComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.EqualityComparer<byte>
	// System.Collections.Generic.EqualityComparer<double>
	// System.Collections.Generic.EqualityComparer<float>
	// System.Collections.Generic.EqualityComparer<int>
	// System.Collections.Generic.EqualityComparer<long>
	// System.Collections.Generic.EqualityComparer<object>
	// System.Collections.Generic.EqualityComparer<sbyte>
	// System.Collections.Generic.EqualityComparer<ulong>
	// System.Collections.Generic.EqualityComparer<ushort>
	// System.Collections.Generic.HashSet.Enumerator<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.HashSet.Enumerator<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.HashSet.Enumerator<UnityEngine.Vector2Int>
	// System.Collections.Generic.HashSet.Enumerator<byte>
	// System.Collections.Generic.HashSet.Enumerator<int>
	// System.Collections.Generic.HashSet.Enumerator<object>
	// System.Collections.Generic.HashSet<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.HashSet<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.HashSet<UnityEngine.Vector2Int>
	// System.Collections.Generic.HashSet<byte>
	// System.Collections.Generic.HashSet<int>
	// System.Collections.Generic.HashSet<object>
	// System.Collections.Generic.HashSetEqualityComparer<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.HashSetEqualityComparer<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.HashSetEqualityComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.HashSetEqualityComparer<byte>
	// System.Collections.Generic.HashSetEqualityComparer<int>
	// System.Collections.Generic.HashSetEqualityComparer<object>
	// System.Collections.Generic.ICollection<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.ICollection<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.ICollection<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.ICollection<StringSlice>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,int>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<byte,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<byte,ushort>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,FMOD.Studio.EventInstance>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBaseRole>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBuff>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJob>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJobLevel>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkill>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSprite>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Code>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,UnityEngine.Color>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,byte>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<long,int>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,FlatBuffers.StringOffset>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,SkillEditorKit.SkillProjectile.Contact>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,double>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,float>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,int>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,long>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,ulong>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,ushort>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<sbyte,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<ushort,object>>
	// System.Collections.Generic.ICollection<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.ICollection<UnityEngine.CombineInstance>
	// System.Collections.Generic.ICollection<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.ICollection<UnityEngine.Vector2>
	// System.Collections.Generic.ICollection<UnityEngine.Vector2Int>
	// System.Collections.Generic.ICollection<UnityEngine.Vector3>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTJob>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.ICollection<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.ICollection<byte>
	// System.Collections.Generic.ICollection<float>
	// System.Collections.Generic.ICollection<int>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.ICollection<ushort>
	// System.Collections.Generic.IComparer<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.IComparer<StringSlice>
	// System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IComparer<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.IComparer<UnityEngine.CombineInstance>
	// System.Collections.Generic.IComparer<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IComparer<UnityEngine.Vector2>
	// System.Collections.Generic.IComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.IComparer<UnityEngine.Vector3>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTJob>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.IComparer<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.IComparer<float>
	// System.Collections.Generic.IComparer<int>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IComparer<ushort>
	// System.Collections.Generic.IDictionary<object,LitJson.ArrayMetadata>
	// System.Collections.Generic.IDictionary<object,LitJson.ObjectMetadata>
	// System.Collections.Generic.IDictionary<object,LitJson.PropertyMetadata>
	// System.Collections.Generic.IDictionary<object,object>
	// System.Collections.Generic.IEnumerable<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.IEnumerable<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.IEnumerable<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.IEnumerable<StringSlice>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,int>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<byte,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<byte,ushort>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,FMOD.Studio.EventInstance>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBaseRole>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBuff>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJob>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJobLevel>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkill>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSprite>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Code>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,UnityEngine.Color>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,byte>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<long,int>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,FlatBuffers.StringOffset>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,SkillEditorKit.SkillProjectile.Contact>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,double>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,float>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,int>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,long>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,ulong>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,ushort>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<sbyte,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<ushort,object>>
	// System.Collections.Generic.IEnumerable<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.IEnumerable<UnityEngine.CombineInstance>
	// System.Collections.Generic.IEnumerable<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IEnumerable<UnityEngine.Playables.PlayableBinding>
	// System.Collections.Generic.IEnumerable<UnityEngine.Vector2>
	// System.Collections.Generic.IEnumerable<UnityEngine.Vector2Int>
	// System.Collections.Generic.IEnumerable<UnityEngine.Vector3>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTJob>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.IEnumerable<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.IEnumerable<byte>
	// System.Collections.Generic.IEnumerable<float>
	// System.Collections.Generic.IEnumerable<int>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerable<ushort>
	// System.Collections.Generic.IEnumerator<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.IEnumerator<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.IEnumerator<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.IEnumerator<StringSlice>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,int>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<byte,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<byte,ushort>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,FMOD.Studio.EventInstance>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBaseRole>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBuff>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJob>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJobLevel>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkill>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSprite>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Code>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,UnityEngine.Color>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,byte>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<long,int>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,FlatBuffers.StringOffset>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,SkillEditorKit.SkillProjectile.Contact>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,double>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,float>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,int>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,long>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,ulong>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,ushort>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<sbyte,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<ushort,object>>
	// System.Collections.Generic.IEnumerator<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.IEnumerator<UnityEngine.CombineInstance>
	// System.Collections.Generic.IEnumerator<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IEnumerator<UnityEngine.Playables.PlayableBinding>
	// System.Collections.Generic.IEnumerator<UnityEngine.Vector2>
	// System.Collections.Generic.IEnumerator<UnityEngine.Vector2Int>
	// System.Collections.Generic.IEnumerator<UnityEngine.Vector3>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTJob>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.IEnumerator<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.IEnumerator<byte>
	// System.Collections.Generic.IEnumerator<float>
	// System.Collections.Generic.IEnumerator<int>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IEnumerator<ushort>
	// System.Collections.Generic.IEqualityComparer<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.IEqualityComparer<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.IEqualityComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.IEqualityComparer<byte>
	// System.Collections.Generic.IEqualityComparer<int>
	// System.Collections.Generic.IEqualityComparer<long>
	// System.Collections.Generic.IEqualityComparer<object>
	// System.Collections.Generic.IEqualityComparer<sbyte>
	// System.Collections.Generic.IEqualityComparer<ushort>
	// System.Collections.Generic.IList<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.IList<StringSlice>
	// System.Collections.Generic.IList<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IList<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.IList<UnityEngine.CombineInstance>
	// System.Collections.Generic.IList<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IList<UnityEngine.Vector2>
	// System.Collections.Generic.IList<UnityEngine.Vector2Int>
	// System.Collections.Generic.IList<UnityEngine.Vector3>
	// System.Collections.Generic.IList<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.IList<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.IList<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.IList<YouYou.DataTable.DTJob>
	// System.Collections.Generic.IList<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.IList<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.IList<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.IList<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.IList<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.IList<float>
	// System.Collections.Generic.IList<int>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.IList<ushort>
	// System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,int>
	// System.Collections.Generic.KeyValuePair<BigWorld.Pooling.PoolKey,object>
	// System.Collections.Generic.KeyValuePair<byte,object>
	// System.Collections.Generic.KeyValuePair<byte,ushort>
	// System.Collections.Generic.KeyValuePair<int,FMOD.Studio.EventInstance>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.KeyValuePair<int,System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.KeyValuePair<int,UnityEngine.Color>
	// System.Collections.Generic.KeyValuePair<int,byte>
	// System.Collections.Generic.KeyValuePair<int,float>
	// System.Collections.Generic.KeyValuePair<int,object>
	// System.Collections.Generic.KeyValuePair<long,int>
	// System.Collections.Generic.KeyValuePair<long,object>
	// System.Collections.Generic.KeyValuePair<object,FlatBuffers.StringOffset>
	// System.Collections.Generic.KeyValuePair<object,SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.KeyValuePair<object,double>
	// System.Collections.Generic.KeyValuePair<object,float>
	// System.Collections.Generic.KeyValuePair<object,int>
	// System.Collections.Generic.KeyValuePair<object,long>
	// System.Collections.Generic.KeyValuePair<object,object>
	// System.Collections.Generic.KeyValuePair<object,ulong>
	// System.Collections.Generic.KeyValuePair<object,ushort>
	// System.Collections.Generic.KeyValuePair<sbyte,object>
	// System.Collections.Generic.KeyValuePair<ushort,object>
	// System.Collections.Generic.LinkedList.Enumerator<int>
	// System.Collections.Generic.LinkedList.Enumerator<object>
	// System.Collections.Generic.LinkedList<int>
	// System.Collections.Generic.LinkedList<object>
	// System.Collections.Generic.LinkedListNode<int>
	// System.Collections.Generic.LinkedListNode<object>
	// System.Collections.Generic.List.Enumerator<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.List.Enumerator<StringSlice>
	// System.Collections.Generic.List.Enumerator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.List.Enumerator<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.List.Enumerator<UnityEngine.CombineInstance>
	// System.Collections.Generic.List.Enumerator<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.List.Enumerator<UnityEngine.Vector2>
	// System.Collections.Generic.List.Enumerator<UnityEngine.Vector2Int>
	// System.Collections.Generic.List.Enumerator<UnityEngine.Vector3>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTJob>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.List.Enumerator<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.List.Enumerator<float>
	// System.Collections.Generic.List.Enumerator<int>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List.Enumerator<ushort>
	// System.Collections.Generic.List<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.List<StringSlice>
	// System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.List<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.List<UnityEngine.CombineInstance>
	// System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.List<UnityEngine.Vector2>
	// System.Collections.Generic.List<UnityEngine.Vector2Int>
	// System.Collections.Generic.List<UnityEngine.Vector3>
	// System.Collections.Generic.List<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.List<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.List<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.List<YouYou.DataTable.DTJob>
	// System.Collections.Generic.List<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.List<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.List<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.List<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.List<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.List<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.List<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.List<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.List<float>
	// System.Collections.Generic.List<int>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.List<ushort>
	// System.Collections.Generic.ObjectComparer<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.ObjectComparer<StringSlice>
	// System.Collections.Generic.ObjectComparer<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.ObjectComparer<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.Generic.ObjectComparer<UnityEngine.CombineInstance>
	// System.Collections.Generic.ObjectComparer<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.ObjectComparer<UnityEngine.Vector2>
	// System.Collections.Generic.ObjectComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.ObjectComparer<UnityEngine.Vector3>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTBaseRole>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTBattleAttr>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTBuff>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTJob>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTJobLevel>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSkill>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSkillLevel>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSprite>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_Audio>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_Code>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_Effect>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_Scene>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.Generic.ObjectComparer<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.Generic.ObjectComparer<float>
	// System.Collections.Generic.ObjectComparer<int>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.Generic.ObjectComparer<ushort>
	// System.Collections.Generic.ObjectEqualityComparer<BigWorld.Pooling.LeaseIdentity>
	// System.Collections.Generic.ObjectEqualityComparer<BigWorld.Pooling.PoolKey>
	// System.Collections.Generic.ObjectEqualityComparer<FMOD.Studio.EventInstance>
	// System.Collections.Generic.ObjectEqualityComparer<FlatBuffers.StringOffset>
	// System.Collections.Generic.ObjectEqualityComparer<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTBaseRole>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTBattleAttr>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTBuff>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTJob>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTJobLevel>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTRoleAnimCategory>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTRoleAnimation>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSkill>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSkillLevel>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSprite>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Audio>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Code>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Effect>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Prefab>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_Scene>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_SceneDetail>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_StorySound>>
	// System.Collections.Generic.ObjectEqualityComparer<System.Nullable<YouYou.DataTable.DTSys_UIForm>>
	// System.Collections.Generic.ObjectEqualityComparer<UnityEngine.Color>
	// System.Collections.Generic.ObjectEqualityComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.ObjectEqualityComparer<byte>
	// System.Collections.Generic.ObjectEqualityComparer<double>
	// System.Collections.Generic.ObjectEqualityComparer<float>
	// System.Collections.Generic.ObjectEqualityComparer<int>
	// System.Collections.Generic.ObjectEqualityComparer<long>
	// System.Collections.Generic.ObjectEqualityComparer<object>
	// System.Collections.Generic.ObjectEqualityComparer<sbyte>
	// System.Collections.Generic.ObjectEqualityComparer<ulong>
	// System.Collections.Generic.ObjectEqualityComparer<ushort>
	// System.Collections.Generic.Queue.Enumerator<int>
	// System.Collections.Generic.Queue.Enumerator<object>
	// System.Collections.Generic.Queue<int>
	// System.Collections.Generic.Queue<object>
	// System.Collections.Generic.Stack.Enumerator<int>
	// System.Collections.Generic.Stack.Enumerator<object>
	// System.Collections.Generic.Stack<int>
	// System.Collections.Generic.Stack<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<SkillEditorKit.SkillProjectile.Contact>
	// System.Collections.ObjectModel.ReadOnlyCollection<StringSlice>
	// System.Collections.ObjectModel.ReadOnlyCollection<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.Animations.AnimationClipPlayable>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.CombineInstance>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.Vector2>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.Vector2Int>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.Vector3>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTBaseRole>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTBattleAttr>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTBuff>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTJob>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTJobLevel>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTRoleAnimCategory>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTRoleAnimation>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSkill>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSkillLevel>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSprite>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_Audio>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_Code>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_Effect>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_Prefab>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_Scene>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_SceneDetail>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_StorySound>
	// System.Collections.ObjectModel.ReadOnlyCollection<YouYou.DataTable.DTSys_UIForm>
	// System.Collections.ObjectModel.ReadOnlyCollection<float>
	// System.Collections.ObjectModel.ReadOnlyCollection<int>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<ushort>
	// System.Comparison<SkillEditorKit.SkillProjectile.Contact>
	// System.Comparison<StringSlice>
	// System.Comparison<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Comparison<UnityEngine.Animations.AnimationClipPlayable>
	// System.Comparison<UnityEngine.CombineInstance>
	// System.Comparison<UnityEngine.EventSystems.RaycastResult>
	// System.Comparison<UnityEngine.Vector2>
	// System.Comparison<UnityEngine.Vector2Int>
	// System.Comparison<UnityEngine.Vector3>
	// System.Comparison<YouYou.DataTable.DTBaseRole>
	// System.Comparison<YouYou.DataTable.DTBattleAttr>
	// System.Comparison<YouYou.DataTable.DTBuff>
	// System.Comparison<YouYou.DataTable.DTJob>
	// System.Comparison<YouYou.DataTable.DTJobLevel>
	// System.Comparison<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Comparison<YouYou.DataTable.DTRoleAnimCategory>
	// System.Comparison<YouYou.DataTable.DTRoleAnimation>
	// System.Comparison<YouYou.DataTable.DTSkill>
	// System.Comparison<YouYou.DataTable.DTSkillLevel>
	// System.Comparison<YouYou.DataTable.DTSprite>
	// System.Comparison<YouYou.DataTable.DTSys_Audio>
	// System.Comparison<YouYou.DataTable.DTSys_Code>
	// System.Comparison<YouYou.DataTable.DTSys_Effect>
	// System.Comparison<YouYou.DataTable.DTSys_Prefab>
	// System.Comparison<YouYou.DataTable.DTSys_Scene>
	// System.Comparison<YouYou.DataTable.DTSys_SceneDetail>
	// System.Comparison<YouYou.DataTable.DTSys_StorySound>
	// System.Comparison<YouYou.DataTable.DTSys_UIForm>
	// System.Comparison<float>
	// System.Comparison<int>
	// System.Comparison<object>
	// System.Comparison<ushort>
	// System.Func<BigWorld.Pooling.RentResult<object>>
	// System.Func<BigWorld.Pooling.WarmResult>
	// System.Func<SkillEditorKit.SkillAttackShape.Pose,SkillEditorKit.SkillAttackShape.Pose,byte>
	// System.Func<System.Collections.Generic.KeyValuePair<int,object>,byte>
	// System.Func<System.Collections.Generic.KeyValuePair<int,object>,int>
	// System.Func<System.Threading.Tasks.VoidTaskResult>
	// System.Func<UnityEngine.PhysicsScene2D,object,int>
	// System.Func<byte>
	// System.Func<double,double>
	// System.Func<int,int,object>
	// System.Func<int,int>
	// System.Func<int,object,object>
	// System.Func<int>
	// System.Func<object,BigWorld.Pooling.RentResult<object>>
	// System.Func<object,BigWorld.Pooling.WarmResult>
	// System.Func<object,System.Threading.Tasks.VoidTaskResult>
	// System.Func<object,byte>
	// System.Func<object,int,object>
	// System.Func<object,int>
	// System.Func<object,object>
	// System.Func<object>
	// System.IEquatable<BigWorld.Pooling.LeaseIdentity>
	// System.IEquatable<BigWorld.Pooling.PoolKey>
	// System.IEquatable<object>
	// System.Linq.Buffer<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Linq.Enumerable.Iterator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Linq.Enumerable.Iterator<object>
	// System.Linq.Enumerable.WhereArrayIterator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Linq.Enumerable.WhereArrayIterator<object>
	// System.Linq.Enumerable.WhereEnumerableIterator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Linq.Enumerable.WhereEnumerableIterator<object>
	// System.Linq.Enumerable.WhereListIterator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Linq.Enumerable.WhereListIterator<object>
	// System.Linq.EnumerableSorter<System.Collections.Generic.KeyValuePair<int,object>,int>
	// System.Linq.EnumerableSorter<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Linq.OrderedEnumerable.<GetEnumerator>d__1<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Linq.OrderedEnumerable<System.Collections.Generic.KeyValuePair<int,object>,int>
	// System.Linq.OrderedEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Nullable<SkillEditorKit.SkillAttackShape.Pose>
	// System.Nullable<StringSlice>
	// System.Nullable<System.ArraySegment<byte>>
	// System.Nullable<UnityEngine.PhysicsScene2D>
	// System.Nullable<UnityEngine.PhysicsScene>
	// System.Nullable<UnityEngine.Vector3>
	// System.Nullable<YouYou.DataTable.DTBaseMonster>
	// System.Nullable<YouYou.DataTable.DTBaseRole>
	// System.Nullable<YouYou.DataTable.DTBattleAttr>
	// System.Nullable<YouYou.DataTable.DTBuff>
	// System.Nullable<YouYou.DataTable.DTChapter>
	// System.Nullable<YouYou.DataTable.DTEquip>
	// System.Nullable<YouYou.DataTable.DTFontColor>
	// System.Nullable<YouYou.DataTable.DTGameLevel>
	// System.Nullable<YouYou.DataTable.DTGameLevelGrade>
	// System.Nullable<YouYou.DataTable.DTGameLevelMonster>
	// System.Nullable<YouYou.DataTable.DTGameLevelRegion>
	// System.Nullable<YouYou.DataTable.DTItem>
	// System.Nullable<YouYou.DataTable.DTJob>
	// System.Nullable<YouYou.DataTable.DTJobLevel>
	// System.Nullable<YouYou.DataTable.DTMaterials>
	// System.Nullable<YouYou.DataTable.DTNPC>
	// System.Nullable<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Nullable<YouYou.DataTable.DTRechargeShop>
	// System.Nullable<YouYou.DataTable.DTRoleAnimCategory>
	// System.Nullable<YouYou.DataTable.DTRoleAnimation>
	// System.Nullable<YouYou.DataTable.DTShop>
	// System.Nullable<YouYou.DataTable.DTShopCategory>
	// System.Nullable<YouYou.DataTable.DTSkill>
	// System.Nullable<YouYou.DataTable.DTSkillLevel>
	// System.Nullable<YouYou.DataTable.DTSprite>
	// System.Nullable<YouYou.DataTable.DTSys_Audio>
	// System.Nullable<YouYou.DataTable.DTSys_Code>
	// System.Nullable<YouYou.DataTable.DTSys_CommonEventId>
	// System.Nullable<YouYou.DataTable.DTSys_Config>
	// System.Nullable<YouYou.DataTable.DTSys_Effect>
	// System.Nullable<YouYou.DataTable.DTSys_Localization>
	// System.Nullable<YouYou.DataTable.DTSys_Prefab>
	// System.Nullable<YouYou.DataTable.DTSys_Scene>
	// System.Nullable<YouYou.DataTable.DTSys_SceneDetail>
	// System.Nullable<YouYou.DataTable.DTSys_StorySound>
	// System.Nullable<YouYou.DataTable.DTSys_UIForm>
	// System.Nullable<YouYou.DataTable.DTTask>
	// System.Nullable<YouYou.DataTable.DTWorldMap>
	// System.Nullable<byte>
	// System.Nullable<double>
	// System.Nullable<float>
	// System.Nullable<int>
	// System.Nullable<long>
	// System.Nullable<sbyte>
	// System.Nullable<short>
	// System.Nullable<uint>
	// System.Nullable<ulong>
	// System.Nullable<ushort>
	// System.Predicate<BigWorld.Pooling.LeaseIdentity>
	// System.Predicate<BigWorld.Pooling.PoolKey>
	// System.Predicate<SkillEditorKit.SkillProjectile.Contact>
	// System.Predicate<StringSlice>
	// System.Predicate<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Predicate<UnityEngine.Animations.AnimationClipPlayable>
	// System.Predicate<UnityEngine.CombineInstance>
	// System.Predicate<UnityEngine.EventSystems.RaycastResult>
	// System.Predicate<UnityEngine.Vector2>
	// System.Predicate<UnityEngine.Vector2Int>
	// System.Predicate<UnityEngine.Vector3>
	// System.Predicate<YouYou.DataTable.DTBaseRole>
	// System.Predicate<YouYou.DataTable.DTBattleAttr>
	// System.Predicate<YouYou.DataTable.DTBuff>
	// System.Predicate<YouYou.DataTable.DTJob>
	// System.Predicate<YouYou.DataTable.DTJobLevel>
	// System.Predicate<YouYou.DataTable.DTPVPSceneMonsterPoint>
	// System.Predicate<YouYou.DataTable.DTRoleAnimCategory>
	// System.Predicate<YouYou.DataTable.DTRoleAnimation>
	// System.Predicate<YouYou.DataTable.DTSkill>
	// System.Predicate<YouYou.DataTable.DTSkillLevel>
	// System.Predicate<YouYou.DataTable.DTSprite>
	// System.Predicate<YouYou.DataTable.DTSys_Audio>
	// System.Predicate<YouYou.DataTable.DTSys_Code>
	// System.Predicate<YouYou.DataTable.DTSys_Effect>
	// System.Predicate<YouYou.DataTable.DTSys_Prefab>
	// System.Predicate<YouYou.DataTable.DTSys_Scene>
	// System.Predicate<YouYou.DataTable.DTSys_SceneDetail>
	// System.Predicate<YouYou.DataTable.DTSys_StorySound>
	// System.Predicate<YouYou.DataTable.DTSys_UIForm>
	// System.Predicate<byte>
	// System.Predicate<float>
	// System.Predicate<int>
	// System.Predicate<object>
	// System.Predicate<ushort>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<BigWorld.Pooling.RentResult<object>>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<BigWorld.Pooling.WarmResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<int>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<object>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<BigWorld.Pooling.RentResult<object>>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<BigWorld.Pooling.WarmResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<int>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<object>
	// System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.RentResult<object>>
	// System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.WarmResult>
	// System.Runtime.CompilerServices.TaskAwaiter<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.TaskAwaiter<byte>
	// System.Runtime.CompilerServices.TaskAwaiter<int>
	// System.Runtime.CompilerServices.TaskAwaiter<object>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<BigWorld.Pooling.RentResult<object>>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<BigWorld.Pooling.WarmResult>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<byte>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<int>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<object>
	// System.Threading.Tasks.Task<BigWorld.Pooling.RentResult<object>>
	// System.Threading.Tasks.Task<BigWorld.Pooling.WarmResult>
	// System.Threading.Tasks.Task<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.Task<byte>
	// System.Threading.Tasks.Task<int>
	// System.Threading.Tasks.Task<object>
	// System.Threading.Tasks.TaskCompletionSource<BigWorld.Pooling.RentResult<object>>
	// System.Threading.Tasks.TaskCompletionSource<BigWorld.Pooling.WarmResult>
	// System.Threading.Tasks.TaskCompletionSource<byte>
	// System.Threading.Tasks.TaskCompletionSource<object>
	// System.Threading.Tasks.TaskFactory<BigWorld.Pooling.RentResult<object>>
	// System.Threading.Tasks.TaskFactory<BigWorld.Pooling.WarmResult>
	// System.Threading.Tasks.TaskFactory<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.TaskFactory<byte>
	// System.Threading.Tasks.TaskFactory<int>
	// System.Threading.Tasks.TaskFactory<object>
	// TMPro.FastAction<object>
	// UnityEngine.Events.InvokableCall<UnityEngine.Vector2>
	// UnityEngine.Events.InvokableCall<byte>
	// UnityEngine.Events.InvokableCall<object,int,int>
	// UnityEngine.Events.InvokableCall<object,object,int>
	// UnityEngine.Events.InvokableCall<object>
	// UnityEngine.Events.InvokableCall<ushort,int>
	// UnityEngine.Events.UnityAction<UnityEngine.Vector2>
	// UnityEngine.Events.UnityAction<byte>
	// UnityEngine.Events.UnityAction<object,int,int>
	// UnityEngine.Events.UnityAction<object,object,int>
	// UnityEngine.Events.UnityAction<object>
	// UnityEngine.Events.UnityAction<ushort,int>
	// UnityEngine.Events.UnityEvent<UnityEngine.Vector2>
	// UnityEngine.Events.UnityEvent<byte>
	// UnityEngine.Events.UnityEvent<object,int,int>
	// UnityEngine.Events.UnityEvent<object,object,int>
	// UnityEngine.Events.UnityEvent<object>
	// UnityEngine.Events.UnityEvent<ushort,int>
	// }}

	public void RefMethods()
	{
		// object Cinemachine.CinemachineVirtualCamera.AddCinemachineComponent<object>()
		// object DG.Tweening.TweenExtensions.Pause<object>(object)
		// object DG.Tweening.TweenExtensions.Play<object>(object)
		// object DG.Tweening.TweenSettingsExtensions.From<object>(object,bool)
		// object DG.Tweening.TweenSettingsExtensions.OnComplete<object>(object,DG.Tweening.TweenCallback)
		// object DG.Tweening.TweenSettingsExtensions.OnKill<object>(object,DG.Tweening.TweenCallback)
		// object DG.Tweening.TweenSettingsExtensions.OnPlay<object>(object,DG.Tweening.TweenCallback)
		// object DG.Tweening.TweenSettingsExtensions.OnStart<object>(object,DG.Tweening.TweenCallback)
		// object DG.Tweening.TweenSettingsExtensions.OnStepComplete<object>(object,DG.Tweening.TweenCallback)
		// object DG.Tweening.TweenSettingsExtensions.OnUpdate<object>(object,DG.Tweening.TweenCallback)
		// object DG.Tweening.TweenSettingsExtensions.SetAutoKill<object>(object,bool)
		// object DG.Tweening.TweenSettingsExtensions.SetDelay<object>(object,float)
		// object DG.Tweening.TweenSettingsExtensions.SetEase<object>(object,DG.Tweening.Ease)
		// object DG.Tweening.TweenSettingsExtensions.SetEase<object>(object,UnityEngine.AnimationCurve)
		// object DG.Tweening.TweenSettingsExtensions.SetId<object>(object,object)
		// object DG.Tweening.TweenSettingsExtensions.SetLoops<object>(object,int,DG.Tweening.LoopType)
		// object DG.Tweening.TweenSettingsExtensions.SetRelative<object>(object)
		// object DG.Tweening.TweenSettingsExtensions.SetRelative<object>(object,bool)
		// object DG.Tweening.TweenSettingsExtensions.SetTarget<object>(object,object)
		// object DG.Tweening.TweenSettingsExtensions.SetUpdate<object>(object,bool)
		// Google.Protobuf.FieldCodec<object> Google.Protobuf.FieldCodec.ForMessage<object>(uint,Google.Protobuf.MessageParser<object>)
		// object LitJson.JsonMapper.ToObject<object>(string)
		// object System.Activator.CreateInstance<object>()
		// object[] System.Array.Empty<object>()
		// System.Void System.Array.Resize<UnityEngine.RaycastHit2D>(UnityEngine.RaycastHit2D[]&,int)
		// System.Void System.Array.Resize<UnityEngine.RaycastHit>(UnityEngine.RaycastHit[]&,int)
		// System.Void System.Array.Resize<object>(object[]&,int)
		// System.Void System.Array.Sort<object>(object[],System.Comparison<object>)
		// System.Collections.Generic.KeyValuePair<int,object> System.Linq.Enumerable.LastOrDefault<System.Collections.Generic.KeyValuePair<int,object>>(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>)
		// System.Linq.IOrderedEnumerable<System.Collections.Generic.KeyValuePair<int,object>> System.Linq.Enumerable.OrderBy<System.Collections.Generic.KeyValuePair<int,object>,int>(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>,System.Func<System.Collections.Generic.KeyValuePair<int,object>,int>)
		// System.Collections.Generic.Dictionary<int,object> System.Linq.Enumerable.ToDictionary<object,int,object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,int>,System.Func<object,object>)
		// System.Collections.Generic.Dictionary<int,object> System.Linq.Enumerable.ToDictionary<object,int,object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,int>,System.Func<object,object>,System.Collections.Generic.IEqualityComparer<int>)
		// System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int,object>> System.Linq.Enumerable.ToList<System.Collections.Generic.KeyValuePair<int,object>>(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>)
		// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>> System.Linq.Enumerable.Where<System.Collections.Generic.KeyValuePair<int,object>>(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>,System.Func<System.Collections.Generic.KeyValuePair<int,object>,bool>)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Where<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.Map2D.GridMapEntityStreamer.<ClosePoolsAsync>d__83>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.Map2D.GridMapEntityStreamer.<ClosePoolsAsync>d__83&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.Pooling.Unity.PoolDriver.<RetryShutdownAsync>d__17>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.Pooling.Unity.PoolDriver.<RetryShutdownAsync>d__17&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.Pooling.Unity.PoolDriver.<ShutdownAsync>d__16>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.Pooling.Unity.PoolDriver.<ShutdownAsync>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.YouYou2D.GameServices2D.<ShutdownInternalAsync>d__45>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.YouYou2D.GameServices2D.<ShutdownInternalAsync>d__45&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.YouYou2D.WorldSession2D.<StopAsync>d__23>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.YouYou2D.WorldSession2D.<StopAsync>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.RentResult<object>>,BigWorld.Map2D.GridMapEntityStreamer.<CompleteSpawnAsync>d__77>(System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.RentResult<object>>&,BigWorld.Map2D.GridMapEntityStreamer.<CompleteSpawnAsync>d__77&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,YouYou.GameEntry.<ShutdownInPhasesAsync>d__128>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,YouYou.GameEntry.<ShutdownInPhasesAsync>d__128&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.Map2D.GridMapEntityStreamer.<ClosePoolsAsync>d__83>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.Map2D.GridMapEntityStreamer.<ClosePoolsAsync>d__83&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.Pooling.Unity.PoolDriver.<RetryShutdownAsync>d__17>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.Pooling.Unity.PoolDriver.<RetryShutdownAsync>d__17&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.Pooling.Unity.PoolDriver.<ShutdownAsync>d__16>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.Pooling.Unity.PoolDriver.<ShutdownAsync>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.YouYou2D.GameServices2D.<ShutdownInternalAsync>d__45>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.YouYou2D.GameServices2D.<ShutdownInternalAsync>d__45&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,BigWorld.YouYou2D.WorldSession2D.<StopAsync>d__23>(System.Runtime.CompilerServices.TaskAwaiter&,BigWorld.YouYou2D.WorldSession2D.<StopAsync>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.RentResult<object>>,BigWorld.Map2D.GridMapEntityStreamer.<CompleteSpawnAsync>d__77>(System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.RentResult<object>>&,BigWorld.Map2D.GridMapEntityStreamer.<CompleteSpawnAsync>d__77&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,YouYou.GameEntry.<ShutdownInPhasesAsync>d__128>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,YouYou.GameEntry.<ShutdownInPhasesAsync>d__128&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,TestAsync.<Test1>d__3>(System.Runtime.CompilerServices.TaskAwaiter&,TestAsync.<Test1>d__3&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<BigWorld.Map2D.GridMapEntityStreamer.<ClosePoolsAsync>d__83>(BigWorld.Map2D.GridMapEntityStreamer.<ClosePoolsAsync>d__83&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<BigWorld.Map2D.GridMapEntityStreamer.<CompleteSpawnAsync>d__77>(BigWorld.Map2D.GridMapEntityStreamer.<CompleteSpawnAsync>d__77&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<BigWorld.Pooling.Unity.PoolDriver.<RetryShutdownAsync>d__17>(BigWorld.Pooling.Unity.PoolDriver.<RetryShutdownAsync>d__17&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<BigWorld.Pooling.Unity.PoolDriver.<ShutdownAsync>d__16>(BigWorld.Pooling.Unity.PoolDriver.<ShutdownAsync>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47>(BigWorld.YouYou2D.GameServices2D.<ShutdownForQuitAsync>d__47&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<BigWorld.YouYou2D.GameServices2D.<ShutdownInternalAsync>d__45>(BigWorld.YouYou2D.GameServices2D.<ShutdownInternalAsync>d__45&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<BigWorld.YouYou2D.WorldSession2D.<StopAsync>d__23>(BigWorld.YouYou2D.WorldSession2D.<StopAsync>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<YouYou.GameEntry.<ShutdownInPhasesAsync>d__128>(YouYou.GameEntry.<ShutdownInPhasesAsync>d__128&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>.Start<TestAsync.<Test1>d__3>(TestAsync.<Test1>d__3&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.RentResult<object>>,BigWorld.Pooling.Samples.PoolingExample.<Start>d__4>(System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.RentResult<object>>&,BigWorld.Pooling.Samples.PoolingExample.<Start>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.WarmResult>,BigWorld.Pooling.Samples.PoolingExample.<Start>d__4>(System.Runtime.CompilerServices.TaskAwaiter<BigWorld.Pooling.WarmResult>&,BigWorld.Pooling.Samples.PoolingExample.<Start>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,TestAsync.<TestMethodAsync>d__2>(System.Runtime.CompilerServices.TaskAwaiter<int>&,TestAsync.<TestMethodAsync>d__2&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<BigWorld.Pooling.Samples.PoolingExample.<Start>d__4>(BigWorld.Pooling.Samples.PoolingExample.<Start>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<TestAsync.<TestMethodAsync>d__2>(TestAsync.<TestMethodAsync>d__2&)
		// object& System.Runtime.CompilerServices.Unsafe.As<object,object>(object&)
		// System.Void* System.Runtime.CompilerServices.Unsafe.AsPointer<object>(object&)
		// System.Threading.Tasks.Task<BigWorld.Pooling.RentResult<object>> System.Threading.Tasks.Task.FromResult<BigWorld.Pooling.RentResult<object>>(BigWorld.Pooling.RentResult<object>)
		// object[] UnityEngine.AssetBundle.ConvertObjects<object>(UnityEngine.Object[])
		// object[] UnityEngine.AssetBundle.LoadAllAssets<object>()
		// object UnityEngine.Component.GetComponent<object>()
		// object UnityEngine.Component.GetComponentInChildren<object>()
		// object UnityEngine.Component.GetComponentInParent<object>()
		// object[] UnityEngine.Component.GetComponents<object>()
		// object[] UnityEngine.Component.GetComponentsInChildren<object>()
		// object[] UnityEngine.Component.GetComponentsInChildren<object>(bool)
		// bool UnityEngine.Component.TryGetComponent<object>(object&)
		// object UnityEngine.GameObject.AddComponent<object>()
		// object UnityEngine.GameObject.GetComponent<object>()
		// object UnityEngine.GameObject.GetComponentInChildren<object>()
		// object UnityEngine.GameObject.GetComponentInChildren<object>(bool)
		// object UnityEngine.GameObject.GetComponentInParent<object>()
		// object UnityEngine.GameObject.GetComponentInParent<object>(bool)
		// object[] UnityEngine.GameObject.GetComponents<object>()
		// object[] UnityEngine.GameObject.GetComponentsInChildren<object>(bool)
		// bool UnityEngine.GameObject.TryGetComponent<object>(object&)
		// object UnityEngine.JsonUtility.FromJson<object>(string)
		// object UnityEngine.Object.FindObjectOfType<object>()
		// object[] UnityEngine.Object.FindObjectsOfType<object>()
		// object UnityEngine.Object.Instantiate<object>(object)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform,bool)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Vector3,UnityEngine.Quaternion)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Vector3,UnityEngine.Quaternion,UnityEngine.Transform)
		// System.Void UnityEngine.Playables.PlayableExtensions.Destroy<UnityEngine.Animations.AnimationClipPlayable>(UnityEngine.Animations.AnimationClipPlayable)
		// System.Void UnityEngine.Playables.PlayableExtensions.Destroy<UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationMixerPlayable)
		// UnityEngine.Playables.PlayableGraph UnityEngine.Playables.PlayableExtensions.GetGraph<UnityEngine.Playables.Playable>(UnityEngine.Playables.Playable)
		// UnityEngine.Playables.Playable UnityEngine.Playables.PlayableExtensions.GetInput<UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationMixerPlayable,int)
		// bool UnityEngine.Playables.PlayableExtensions.IsValid<UnityEngine.Animations.AnimationClipPlayable>(UnityEngine.Animations.AnimationClipPlayable)
		// bool UnityEngine.Playables.PlayableExtensions.IsValid<UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationMixerPlayable)
		// System.Void UnityEngine.Playables.PlayableExtensions.Play<UnityEngine.Playables.Playable>(UnityEngine.Playables.Playable)
		// System.Void UnityEngine.Playables.PlayableExtensions.SetInputWeight<UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationMixerPlayable,int,float)
		// System.Void UnityEngine.Playables.PlayableExtensions.SetSpeed<UnityEngine.Animations.AnimationClipPlayable>(UnityEngine.Animations.AnimationClipPlayable,double)
		// System.Void UnityEngine.Playables.PlayableExtensions.SetTime<UnityEngine.Animations.AnimationClipPlayable>(UnityEngine.Animations.AnimationClipPlayable,double)
		// System.Void UnityEngine.Playables.PlayableExtensions.SetTime<UnityEngine.Playables.Playable>(UnityEngine.Playables.Playable,double)
		// bool UnityEngine.Playables.PlayableGraph.Connect<UnityEngine.Animations.AnimationClipPlayable,UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationClipPlayable,int,UnityEngine.Animations.AnimationMixerPlayable,int)
		// bool UnityEngine.Playables.PlayableGraph.Connect<UnityEngine.Animations.AnimationMixerPlayable,UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationMixerPlayable,int,UnityEngine.Animations.AnimationMixerPlayable,int)
		// System.Void UnityEngine.Playables.PlayableGraph.Disconnect<UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationMixerPlayable,int)
		// int UnityEngine.Playables.PlayableOutputExtensions.GetSourceOutputPort<UnityEngine.Animations.AnimationPlayableOutput>(UnityEngine.Animations.AnimationPlayableOutput)
		// System.Void UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable<UnityEngine.Animations.AnimationPlayableOutput,UnityEngine.Animations.AnimationClipPlayable>(UnityEngine.Animations.AnimationPlayableOutput,UnityEngine.Animations.AnimationClipPlayable,int)
		// System.Void UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable<UnityEngine.Animations.AnimationPlayableOutput,UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationPlayableOutput,UnityEngine.Animations.AnimationMixerPlayable)
		// System.Void UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable<UnityEngine.Animations.AnimationPlayableOutput,UnityEngine.Animations.AnimationMixerPlayable>(UnityEngine.Animations.AnimationPlayableOutput,UnityEngine.Animations.AnimationMixerPlayable,int)
		// object[] UnityEngine.Resources.ConvertObjects<object>(UnityEngine.Object[])
		// object UnityEngine.Resources.GetBuiltinResource<object>(string)
		// object UnityEngine.Resources.Load<object>(string)
		// UnityEngine.ResourceRequest UnityEngine.Resources.LoadAsync<object>(string)
		// object UnityEngine.ScriptableObject.CreateInstance<object>()
		// object UnityEngine.Tilemaps.ITilemap.GetComponent<object>()
	}
}