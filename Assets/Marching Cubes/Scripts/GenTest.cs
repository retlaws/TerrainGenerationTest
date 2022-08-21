using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class GenTest : MonoBehaviour
{

	[Header("Init Settings")]
	public int numChunks = 4;

	[Tooltip("This dictates the number of vertices in the chunk")]
	public int numPointsPerAxis = 10;

	[Tooltip("This dictates the actual dimensions of the world")]
	public float boundsSize = 10;
	public float isoLevel = 0f;
	public bool useFlatShading;

	public float noiseScale;
	public float noiseHeightMultiplier;
	public bool blurMap;
	public int blurRadius = 3;

	[Header("References")]
	public ComputeShader meshCompute;
	public ComputeShader densityCompute;
	public ComputeShader blurCompute;
	public ComputeShader editTextureCompute;
	public Material material;

	// Private
	ComputeBuffer triangleBuffer;
	ComputeBuffer triCountBuffer;
	[HideInInspector] public RenderTexture rawDensityTexture;
	[HideInInspector] public RenderTexture processedDensityTexture;
	Chunk[] chunks;

	VertexData[] vertexDataArray;

	int totalVerts;

	// Stopwatches
	RenderTexture originalMap;

	void Start()
	{
		InitTextures(); // this creates empty 3D textures for the "world" to be put in
		CreateBuffers(); // this creates the buffers to used to send the data to the compute shaders
		CreateChunks(); // this creates the and array (chunks[]) of empty GameObject chunks
		ComputeDensity(); // TODO this creates the map data - I think that we can remove this and insert our own map data here
		GenerateAllChunks();

		ComputeHelper.CreateRenderTexture3D(ref originalMap, processedDensityTexture);
		ComputeHelper.CopyRenderTexture3D(processedDensityTexture, originalMap);
	}

	void Update()
	{
		// TODO: move somewhere more sensible
		material.SetTexture("DensityTex", originalMap);
		material.SetFloat("planetBoundsSize", boundsSize);
	}

	void InitTextures()
	{
		// Explanation of texture size:
		// Each pixel maps to one point.
		// Each chunk has "numPointsPerAxis" points along each axis
		// The last points of each chunk overlap in space with the first points of the next chunk
		// Therefore we need one fewer pixel than points for each added chunk
		int size = numChunks * (numPointsPerAxis - 1) + 1;
		Create3DTexture(ref rawDensityTexture, size, "Raw Density Texture");
		Create3DTexture(ref processedDensityTexture, size, "Processed Density Texture"); 

		if (!blurMap)
		{
			processedDensityTexture = rawDensityTexture;
		}

		// Set textures on compute shaders
		densityCompute.SetTexture(0, "DensityTexture", rawDensityTexture);
		editTextureCompute.SetTexture(0, "EditTexture", rawDensityTexture);
		blurCompute.SetTexture(0, "Source", rawDensityTexture);
		blurCompute.SetTexture(0, "Result", processedDensityTexture);
		meshCompute.SetTexture(0, "DensityTexture", blurCompute ? processedDensityTexture : rawDensityTexture);
	}

	void GenerateAllChunks()
	{
		totalVerts = 0;
		for (int i = 0; i < chunks.Length; i++)
		{
			GenerateChunk(chunks[i]);
		}
	}

	void ComputeDensity()
	{
		// Get points (each point is a vector4: xyz = position, w = density)
		int textureSize = rawDensityTexture.width;

		densityCompute.SetInt("textureSize", textureSize); //this is telling the compute shader the size of the 3D texture that it is working with
		densityCompute.SetFloat("planetSize", boundsSize); // this is telling the compute shader the size of the world - I assume the diameter or radius of the sphere 
		densityCompute.SetFloat("noiseScale", noiseScale);
		densityCompute.SetFloat("noiseHeightMultiplier", noiseHeightMultiplier);


		ComputeHelper.Dispatch(densityCompute, textureSize, textureSize, textureSize);

		ProcessDensityMap();
	}

	void ProcessDensityMap()
	{
		if (blurMap)
		{
			int size = rawDensityTexture.width;
			blurCompute.SetInts("brushCentre", 0, 0, 0);
			blurCompute.SetInt("blurRadius", blurRadius);
			blurCompute.SetInt("textureSize", rawDensityTexture.width);
			ComputeHelper.Dispatch(blurCompute, size, size, size);
		}
	}

	void GenerateChunk(Chunk chunk)
	{
		// Marching cubes
		int numVoxelsPerAxis = numPointsPerAxis - 1;
		int marchKernel = 0;

		//meshCompute is the marching cubes algorthim

		meshCompute.SetInt("textureSize", processedDensityTexture.width);
		meshCompute.SetInt("numPointsPerAxis", numPointsPerAxis);
		meshCompute.SetFloat("isoLevel", isoLevel);
		meshCompute.SetFloat("planetSize", boundsSize);
		triangleBuffer.SetCounterValue(0);
		meshCompute.SetBuffer(marchKernel, "triangles", triangleBuffer);

		Vector3 chunkCoord = (Vector3)chunk.id * (numPointsPerAxis - 1);
		meshCompute.SetVector("chunkCoord", chunkCoord);

		ComputeHelper.Dispatch(meshCompute, numVoxelsPerAxis, numVoxelsPerAxis, numVoxelsPerAxis, marchKernel);

		// Create mesh
		int[] vertexCountData = new int[1];
		triCountBuffer.SetData(vertexCountData);
		ComputeBuffer.CopyCount(triangleBuffer, triCountBuffer, 0);

		triCountBuffer.GetData(vertexCountData);

		int numVertices = vertexCountData[0] * 3;

		// Fetch vertex data from GPU
		triangleBuffer.GetData(vertexDataArray, 0, 0, numVertices);

		//CreateMesh(vertices);
		chunk.CreateMesh(vertexDataArray, numVertices, useFlatShading);
	}

	void CreateBuffers()
	{
		//TODO - this only works with a square world / 3D texture
		int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;
		int numVoxelsPerAxis = numPointsPerAxis - 1;
		int numVoxels = numVoxelsPerAxis * numVoxelsPerAxis * numVoxelsPerAxis;
		int maxTriangleCount = numVoxels * 5;
		int maxVertexCount = maxTriangleCount * 3;

		triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
		triangleBuffer = new ComputeBuffer(maxVertexCount, ComputeHelper.GetStride<VertexData>(), ComputeBufferType.Append);
		vertexDataArray = new VertexData[maxVertexCount];
	}

	void ReleaseBuffers()
	{
		ComputeHelper.Release(triangleBuffer, triCountBuffer);
	}


	void CreateChunks()
	{
		chunks = new Chunk[numChunks * numChunks * numChunks];
		float chunkSize = boundsSize / numChunks;
		int i = 0;

		for (int y = 0; y < numChunks; y++)
		{
			for (int x = 0; x < numChunks; x++)
			{
				for (int z = 0; z < numChunks; z++)
				{
					Vector3Int coord = new Vector3Int(x, y, z);
					float posX = (-(numChunks - 1f) / 2 + x) * chunkSize;
					float posY = (-(numChunks - 1f) / 2 + y) * chunkSize;
					float posZ = (-(numChunks - 1f) / 2 + z) * chunkSize;
					Vector3 centre = new Vector3(posX, posY, posZ);

					GameObject meshHolder = new GameObject($"Chunk ({x}, {y}, {z})");
					meshHolder.transform.parent = transform;
					meshHolder.layer = gameObject.layer;

					Chunk chunk = new Chunk(coord, centre, chunkSize, numPointsPerAxis, meshHolder);
					chunk.SetMaterial(material);
					chunks[i] = chunk;
					i++;
				}
			}
		}
	}


	public void Terraform(Vector3 point, float weight, float radius)
	{
		int editTextureSize = rawDensityTexture.width;
		float editPixelWorldSize = boundsSize / editTextureSize;
		int editRadius = Mathf.CeilToInt(radius / editPixelWorldSize);

		float tx = Mathf.Clamp01((point.x + boundsSize / 2) / boundsSize);
		float ty = Mathf.Clamp01((point.y + boundsSize / 2) / boundsSize);
		float tz = Mathf.Clamp01((point.z + boundsSize / 2) / boundsSize);

		int editX = Mathf.RoundToInt(tx * (editTextureSize - 1));
		int editY = Mathf.RoundToInt(ty * (editTextureSize - 1));
		int editZ = Mathf.RoundToInt(tz * (editTextureSize - 1));

		editTextureCompute.SetFloat("weight", weight);
		editTextureCompute.SetFloat("deltaTime", Time.deltaTime);
		editTextureCompute.SetInts("brushCentre", editX, editY, editZ);
		editTextureCompute.SetInt("brushRadius", editRadius);

		editTextureCompute.SetInt("size", editTextureSize);
		ComputeHelper.Dispatch(editTextureCompute, editTextureSize, editTextureSize, editTextureSize);

		//ProcessDensityMap();
		int size = rawDensityTexture.width;

		if (blurMap)
		{
			blurCompute.SetInt("textureSize", rawDensityTexture.width);
			blurCompute.SetInts("brushCentre", editX - blurRadius - editRadius, editY - blurRadius - editRadius, editZ - blurRadius - editRadius);
			blurCompute.SetInt("blurRadius", blurRadius);
			blurCompute.SetInt("brushRadius", editRadius);
			int k = (editRadius + blurRadius) * 2;
			ComputeHelper.Dispatch(blurCompute, k, k, k);
		}



		float worldRadius = (editRadius + 1 + ((blurMap) ? blurRadius : 0)) * editPixelWorldSize;
		for (int i = 0; i < chunks.Length; i++)
		{
			Chunk chunk = chunks[i];
			if (MathUtility.SphereIntersectsBox(point, worldRadius, chunk.centre, Vector3.one * chunk.size))
			{

				chunk.terra = true;
				GenerateChunk(chunk);

			}
		}
	}

	//TODO - we could take three sizes as parameters in this function and this would
	// allow us to make a non-square 3D texture so rather than just taking size, 
	// we could take in width, height and depth. 

	void Create3DTexture(ref RenderTexture texture, int size, string name) 
	{
		var format = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat;

		if (texture == null || !texture.IsCreated() || texture.width != size || texture.height != size || texture.volumeDepth != size || texture.graphicsFormat != format)
		{
			if (texture != null)
			{
				texture.Release();
			}
			const int numBitsInDepthBuffer = 0;
			texture = new RenderTexture(size, size, numBitsInDepthBuffer);
			texture.graphicsFormat = format;
			texture.volumeDepth = size;
			texture.enableRandomWrite = true;
			texture.dimension = TextureDimension.Tex3D;

			texture.Create();
		}
		texture.wrapMode = TextureWrapMode.Repeat;
		texture.filterMode = FilterMode.Bilinear;
		texture.name = name;
	}

	void OnDestroy()
	{
		ReleaseBuffers();
		foreach (Chunk chunk in chunks)
		{
			chunk.Release();
		}
	}
}