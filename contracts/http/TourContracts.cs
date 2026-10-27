namespace SmartStay.Contracts.Http;

/// <summary>
/// Enlace entre nodos del recorrido virtual 360°.
/// </summary>
public sealed record TourLink(
    string NodeId,
    double Yaw,
    double Pitch
);

/// <summary>
/// Nodo/panorama equirectangular dentro de un recorrido 360°.
/// </summary>
public sealed record TourNode(
    string Id,
    string Panorama,
    string Name,
    IReadOnlyList<TourLink> Links
);

/// <summary>
/// Manifiesto validado del recorrido 360° para una habitación.
/// </summary>
public sealed record RoomTourManifest(
    int RoomId,
    string StartNodeId,
    IReadOnlyList<TourNode> Nodes
);
