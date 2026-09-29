using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class PointComponent : MonoBehaviour, IDeselectHandler
{
    public void OnDeselect(BaseEventData eventData)
    {
        //Debug.LogError(eventData.selectedObject.gameObject.name);
        gameObject.SetActive(false);
    }
}