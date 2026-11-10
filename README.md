# Shear and Grow

Monorepository for the Unity client and PlayFab / Azure Functions backend.

- `client/`: Unity project. Open this directory in Unity Hub (6000.6.8f1).
- `backend/`: server implementation (not implemented yet).
- `Documentation/`: game documentation and references.
- `SheepHaircutProject.slnx`: manually maintained solution for integration work.

After opening the client, regenerate C# project files in Unity under Preferences > External Tools. Unity-generated `.csproj` files are not committed. Build and deploy the backend independently of the mobile client.
