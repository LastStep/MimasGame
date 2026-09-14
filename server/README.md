# Mimas.Server

ASP.NET Core WebSocket game server (authoritative). Run locally:

    dotnet run --project server/Mimas.Server
    curl http://localhost:7777/health

WebSocket smoke test from a browser console:

    const ws = new WebSocket("ws://localhost:7777/ws");
    ws.onmessage = e => console.log(e.data);
    ws.onopen = () => ws.send(JSON.stringify({ t: "ping", p: {} }));   // → {"t":"pong","p":{}}

Docker (from repo root): `docker build -f server/Dockerfile -t mimas-server . && docker run -p 127.0.0.1:7777:7777 mimas-server`
