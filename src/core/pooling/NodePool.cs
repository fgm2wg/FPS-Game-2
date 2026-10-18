using System;
using System.Collections.Generic;
using Godot;

namespace FPSGame.Core.Pooling;

/// <summary>
/// High-performance zero-allocation object pool for recurrent Node3D gameplay actors
/// (grenades, explosion effects, projectile casings, impact decals, particles).
/// </summary>
public class NodePool<T> where T : Node3D
{
    private readonly Stack<T> _available = new();
    private readonly Func<T> _factory;
    private readonly Node _parent;

    public int TotalCreated { get; private set; }
    public int AvailableCount => _available.Count;

    public NodePool(Node parent, Func<T> factory, int prewarmCount = 0)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        Prewarm(prewarmCount);
    }

    public void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            T item = CreateNew();
            Deactivate(item);
            _available.Push(item);
        }
    }

    private T CreateNew()
    {
        T item = _factory();
        _parent.AddChild(item);
        TotalCreated++;
        return item;
    }

    public T Checkout(Vector3 globalPosition)
    {
        T item = _available.Count > 0 ? _available.Pop() : CreateNew();
        Activate(item, globalPosition);
        return item;
    }

    public void Return(T item)
    {
        if (item == null) return;
        if (item is CollisionObject3D)
        {
            item.Visible = false;
            item.SetDeferred(Node.PropertyName.ProcessMode, (int)Node.ProcessModeEnum.Disabled);
        }
        else
        {
            Deactivate(item);
        }
        _available.Push(item);
    }

    private static void Activate(T item, Vector3 position)
    {
        if (item.IsInsideTree())
        {
            item.GlobalPosition = position;
        }
        else
        {
            item.Position = position;
        }
        item.Visible = true;
        item.ProcessMode = Node.ProcessModeEnum.Inherit;
    }

    private static void Deactivate(T item)
    {
        item.Visible = false;
        item.ProcessMode = Node.ProcessModeEnum.Disabled;
    }
}

