using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Terraformer : MonoBehaviour
{
	[SerializeField] LayerMask terrainMask;
	[SerializeField] float terraformRadius = 5;
	[SerializeField] float terraformSpeedNear = 0.1f;
	[SerializeField] float terraformSpeedFar = 0.25f;
	[SerializeField] bool allowAddingOfTerrain = true;
	[Range(0, 1000)]
	[SerializeField] float toolStrength; 

	GenTest genTest;
	FirstPersonController firstPersonController;

	bool isTerraforming;
	Transform cam;
	Vector3 lastTerraformPointLocal;

	public event System.Action onTerrainModified;

	void Start()
	{
		genTest = FindObjectOfType<GenTest>();
		cam = Camera.main.transform;
		firstPersonController = FindObjectOfType<FirstPersonController>();
	}

	void Update() // TODO this needs changing so that it is only doing this if the player presses a mouse button
	{
		RaycastHit hit;

		bool wasTerraformingLastFrame = isTerraforming;
		isTerraforming = false;

		int numIterations = 5;
		bool rayHitTerrain = false;

		for (int i = 0; i < numIterations; i++)
		{
			float rayRadius = terraformRadius * Mathf.Lerp(0.01f, 1, i / (numIterations - 1f));
			if (Physics.SphereCast(cam.position, rayRadius, cam.forward, out hit, 1000, terrainMask))
			{
				lastTerraformPointLocal = MathUtility.WorldToLocalVector(cam.rotation, hit.point);
				Terraform(hit.point);
				rayHitTerrain = true;
				break;
			}
		}

		if (!rayHitTerrain && wasTerraformingLastFrame)
		{
			Vector3 terraformPoint = MathUtility.LocalToWorldVector(cam.rotation, lastTerraformPointLocal);
			Terraform(terraformPoint);
		}

	}

	//TODO this needs changing so that the player input is not being taken in here

	void Terraform(Vector3 terraformPoint)
	{
		float weight = toolStrength; // SW added 

		// Add terrain
		if (Input.GetMouseButton(1) && allowAddingOfTerrain)
		{
			isTerraforming = true;
			genTest.Terraform(terraformPoint, -weight, terraformRadius);
			firstPersonController.NotifyTerrainChanged(terraformPoint, terraformRadius);
		}
		// Subtract terrain
		else if (Input.GetMouseButton(0))
		{
			isTerraforming = true;
			genTest.Terraform(terraformPoint, weight, terraformRadius);
		}

		if (isTerraforming)
		{
			onTerrainModified?.Invoke();
		}
	}
}
