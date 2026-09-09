# API request files

Quick manual smoke-tests for the `SpaceTravel.Api` HTTP endpoints, one `.http`
file per feature. Open with the VS Code [REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client)
extension (or Visual Studio's built-in `.http` editor) and click **Send Request**
above each request.

1. Start the API: `dotnet run --project src/backend/SpaceTravel.Api` (listens on
   `http://localhost:5095`, see `Properties/launchSettings.json`).
2. Open any file below and send requests top to bottom.

| File | Endpoint |
| --- | --- |
| `get-planets.http` | `GET /api/planets` |
| `get-fleet.http` | `GET /api/shuttles` |
| `call-shuttle.http` | `POST /api/travel-requests` (+ validation-error examples) |
| `get-travel-request.http` | `GET /api/travel-requests/{id}` |
| `get-travel-history.http` | `GET /api/travel-history` |
| `get-travel-stats.http` | `GET /api/travel-history/stats` |

`call-shuttle.http` returns a `travelRequestId` you can paste into
`get-travel-request.http` to poll that call's status.

`http-client.env.json` defines a `dev` environment (`baseUrl`) if you'd rather
select an environment than use the `@baseUrl` variable declared at the top of
each file.
