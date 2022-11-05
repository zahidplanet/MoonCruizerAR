using System;

using Niantic.ARDK.Utilities.Collections;

using UnityEngine;

using System.Collections.Generic;

using UnityEditor;

namespace Niantic.ARDK.AR.WayspotAnchors
{
  [Serializable]
  public sealed class AuthoredWayspotAnchorData
  {

    [Serializable]
    public class PrefabData
    {
      [SerializeField]
      private GameObject _asset;

      [SerializeField]
      private bool _isVisible;

      public GameObject Asset
      {
        get => _asset;
        internal set => _asset = value;
      }

      public bool IsVisible
      {
        get => _isVisible;
        internal set => _isVisible = value;
      }

      // This class is used as a key in dictionaries.
      // Due to how there are separate instances of this class (and its parent class)
      // in the VPSLocationManifest's anchors array and the AuthoredWayspotAnchor class, and how
      // the AuthoredWayspotAnchor's instances are recreated each time ResetToData is called,
      // it's a requirement that both:
      // (1) Equality is done by value, instead of by reference
      // (2) Hash remains constant, even as values change.
      // The solution is to use this Identifier.
      [SerializeField]
      private string _identifier;

      public string Identifier { get => _identifier; }

      private PrefabData() {}
      internal PrefabData(string identifier, GameObject asset, bool isVisible)
      {
        _identifier = identifier;
        Asset = asset;
        IsVisible = isVisible;
      }

      public PrefabData(GameObject asset)
      {
        _identifier = Guid.NewGuid().ToString();
        Asset = asset;
        IsVisible = true;
      }

      public override bool Equals(object obj) =>
        this.Equals(obj as PrefabData);

      public bool Equals(PrefabData other)
      {
        if (other is null)
          return false;

        // Optimization for a common success case.
        if (System.Object.ReferenceEquals(this, other))
          return true;

        if (this.GetType() != other.GetType())
          return false;

        return string.Equals(Identifier, other.Identifier);
      }

      public bool ValuesDifferFrom(PrefabData other)
      {
        return
          !Identifier.Equals(other.Identifier)
          || IsVisible != other.IsVisible
          || !Equals(Asset, other.Asset);
      }

      public override int GetHashCode()
      {
        return Identifier.GetHashCode();
      }

      public PrefabData Copy()
      {
        return new PrefabData(Identifier, Asset, IsVisible);
      }

      public void Reset()
      {
        Asset = null;
        IsVisible = true;
      }
    }

    // All members are readonly so that the serialized version is always correct
    [SerializeField]
    private string _name;

    [SerializeField]
    private string _identifier;

    [SerializeField]
    private string _payload;

    [SerializeField]
    private Vector3 _position;

    [SerializeField]
    private Vector3 _rotation;

    [SerializeField]
    private Vector3 _scale = Vector3.one;

    [SerializeField]
    private string _tags;

    [SerializeField]
    private PrefabData[] _associatedPrefabs;

    [SerializeField]
    private string _manifestIdentifier;

    public string Name { get => _name; }

    public string Identifier { get =>_identifier;}

    public string Payload { get => _payload; }

    public Vector3 Position { get => _position; }

    public Vector3 Rotation { get => _rotation; }

    public Vector3 Scale { get => _scale; }

    /// Multiple string tags delineated by commas
    public string Tags { get => _tags; }

    public IReadOnlyList<PrefabData> AssociatedPrefabs
    {
      get
      {
        if (_readonlyPrefabs == null)
          _readonlyPrefabs = _associatedPrefabs.AsNonNullReadOnly();

        return _readonlyPrefabs;
      }
    }

    private IReadOnlyList<PrefabData> _readonlyPrefabs;

    internal string _ManifestIdentifier { get => _manifestIdentifier; }

    public AuthoredWayspotAnchorData(string manifestIdentifier)
    {
      _manifestIdentifier = manifestIdentifier;
    }

    internal AuthoredWayspotAnchorData
    (
      string name,
      string identifier,
      string payload,
      Vector3 position,
      Vector3 rotation,
      Vector3 scale,
      string tags,
      PrefabData[] prefabs,
      string manifestIdentifier
    )
    : this(manifestIdentifier)
    {
      _name = name;
      _identifier = identifier;
      _payload = payload;
      _position = position;
      _rotation = rotation;
      _scale = scale;
      _tags = tags;
      _associatedPrefabs = prefabs;
    }

    public override string ToString()
    {
      return $"Anchor {Name}({(string.IsNullOrEmpty(Payload) ? "No Payload" : Payload.Substring(0, 5))} @ P: {Position} / R: {Rotation} - Tags: {Tags}";
    }
  }
}
