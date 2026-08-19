### GitHub Container Registry (GHCR) Delivery

The Plaxion platform images are published to GitHub Container Registry.

#### Image Naming
`ghcr.io/${OWNER}/plaxion-platform`

#### Tagging Policy
- **SemVer**: Images pushed via a release tag (e.g. `v1.0.0`) are tagged with the version.
- **SHA**: Every release build is tagged with the Git SHA for traceability.
- **Latest**: The `latest` tag always points to the most recent stable (non-prerelease) release from the default branch.

#### CI/CD Workflows
- **CI (`ci.yml`)**: Triggered on pull requests. Performs .NET and Angular builds/tests and a Docker build validation. Does not push to GHCR.
- **Release (`release.yml`)**: Triggered on new version tags (`v*.*.*`). Builds and pushes the multi-role image to GHCR.

#### Helm Consumption
Future Helm charts or Kubernetes manifests should consume this image by specifying the `ROLE` environment variable in the container spec. 
Configurable items like `API_BASE_URL` or `ConnectionStrings__Platform` should be mapped via ConfigMaps or Secrets.
