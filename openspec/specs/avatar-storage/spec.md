# avatar-storage Specification

## Purpose

Private, S3-compatible object storage for user avatars: a RustFS resource in the local Aspire AppHost with a non-public bucket, validated uploads through the application, and short-lived presigned URLs for serving. The profile's picture value is a stable application route, never a directly stored object URL.

## Requirements

### Requirement: Private avatar bucket in the AppHost
The Aspire AppHost SHALL run RustFS with an avatars bucket provisioned at startup. The bucket SHALL NOT be publicly readable; all object access SHALL flow through application-mediated requests or presigned URLs.

#### Scenario: Bucket exists and is not public
- **WHEN** the AppHost starts in Development
- **THEN** the RustFS container becomes healthy, the avatars bucket is provisioned, and anonymous object requests are refused

### Requirement: Server-mediated validated upload
Avatar uploads SHALL be accepted only through an authenticated application endpoint. The endpoint SHALL validate content type and size before storing the object under the uploading user's subject key. Unauthenticated upload attempts SHALL be refused.

#### Scenario: Upload stores under the user's key
- **WHEN** a signed-in user uploads a valid avatar image
- **THEN** the object is stored under the user's subject key and the profile's picture attribute points at the user's stable avatar route

#### Scenario: Unauthenticated upload is refused
- **WHEN** an unauthenticated request posts an avatar upload
- **THEN** the request is redirected to the sign-in screen and no object is stored

### Requirement: Presigned redirect serving
The stable avatar route SHALL respond to an authenticated request by redirecting to a freshly generated presigned GET URL with a short validity period. When no avatar object exists for the user, the route SHALL serve a placeholder instead of an error. The profile SHALL NOT store presigned URLs.

#### Scenario: Avatar request redirects to a presigned URL
- **WHEN** a signed-in user's browser requests their stable avatar route
- **THEN** the response redirects to a presigned URL that serves the object

#### Scenario: Missing avatar yields a placeholder
- **WHEN** a user without an uploaded avatar is displayed
- **THEN** an avatar placeholder is shown rather than a broken image or an error
