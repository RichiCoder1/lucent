use std::ffi::c_char;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Instant;
use taffy::prelude::*;
use taffy::{Overflow, Point, TaffyError};

static LIVE: AtomicUsize = AtomicUsize::new(0);

pub struct Probe;

#[unsafe(no_mangle)]
pub extern "C" fn lucent_taffy_create() -> *mut Probe {
    LIVE.fetch_add(1, Ordering::SeqCst);
    Box::into_raw(Box::new(Probe))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn lucent_taffy_destroy(probe: *mut Probe) {
    if probe.is_null() {
        return;
    }
    unsafe { drop(Box::from_raw(probe)) };
    LIVE.fetch_sub(1, Ordering::SeqCst);
}

#[unsafe(no_mangle)]
pub extern "C" fn lucent_taffy_live() -> usize {
    LIVE.load(Ordering::SeqCst)
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn lucent_taffy_run(
    probe: *mut Probe,
    output: *mut c_char,
    capacity: usize,
) -> i32 {
    if probe.is_null() || output.is_null() || capacity == 0 {
        return -1;
    }
    match evaluate() {
        Ok(value) if value.len() + 1 <= capacity => {
            unsafe {
                std::ptr::copy_nonoverlapping(value.as_ptr(), output.cast::<u8>(), value.len());
                *output.add(value.len()) = 0;
            }
            value.len() as i32
        }
        Ok(_) => -2,
        Err(_) => -3,
    }
}

fn evaluate() -> Result<String, TaffyError> {
    let started = Instant::now();
    let wide = shell(1060.0, &[184.0, 320.0, 556.0])?;
    let medium = shell(840.0, &[64.0, 300.0, 476.0])?;
    let flex = flex_toolbar()?;
    let wrapped = wrapped_chips()?;
    let paragraph = wrapped_text()?;
    let nested = nested_scroll()?;
    let virtual_rows = ((128.0_f32 + 472.0) / 64.0).ceil() as usize + 2;
    let rounded = cumulative_rounding(101.0, 3, 1.5);

    let mut tree = TaffyTree::<()>::new();
    let leaf = tree.new_leaf(Style::default())?;
    let root = tree.new_with_children(
        Style {
            display: Display::Flex,
            size: Size {
                width: length(1000.0),
                height: length(600.0),
            },
            ..Default::default()
        },
        &[leaf],
    )?;
    for _ in 0..999 {
        let child = tree.new_leaf(Style {
            size: Size {
                width: length(20.0),
                height: length(20.0),
            },
            ..Default::default()
        })?;
        tree.add_child(root, child)?;
    }
    tree.compute_layout(root, Size::MAX_CONTENT)?;
    let elapsed = started.elapsed().as_micros();
    Ok(format!(
        "{{\"taffy\":\"0.14.0\",\"wide\":{:?},\"medium\":{:?},\"flex\":{:?},\"wrapped_flex_lines\":{},\"paragraph\":{{\"width\":{},\"height\":{},\"measure_calls\":{}}},\"nested_scroll\":{{\"viewport_height\":{},\"content_height\":{},\"scroll_extent\":{}}},\"virtual_last_exclusive\":{},\"rounded_tracks\":{:?},\"nodes\":{},\"elapsed_us\":{}}}",
        wide,
        medium,
        flex,
        wrapped,
        paragraph.0,
        paragraph.1,
        paragraph.2,
        nested.0,
        nested.1,
        nested.2,
        virtual_rows,
        rounded,
        tree.total_node_count(),
        elapsed
    ))
}

fn shell(width: f32, columns: &[f32; 3]) -> Result<Vec<f32>, TaffyError> {
    let mut tree = TaffyTree::<()>::new();
    let capture = tree.new_leaf(Style {
        grid_row: line(1),
        grid_column: span(3),
        size: Size {
            width: auto(),
            height: length(48.0),
        },
        ..Default::default()
    })?;
    let nav = tree.new_leaf(Style {
        grid_row: line(2),
        grid_column: line(1),
        ..Default::default()
    })?;
    let list = tree.new_leaf(Style {
        grid_row: line(2),
        grid_column: line(2),
        ..Default::default()
    })?;
    let editor = tree.new_leaf(Style {
        grid_row: line(2),
        grid_column: line(3),
        ..Default::default()
    })?;
    let root = tree.new_with_children(
        Style {
            display: Display::Grid,
            size: Size {
                width: length(width),
                height: length(520.0),
            },
            grid_template_columns: vec![length(columns[0]), length(columns[1]), fr(1.0)],
            grid_template_rows: vec![length(48.0), fr(1.0)],
            ..Default::default()
        },
        &[capture, nav, list, editor],
    )?;
    tree.compute_layout(root, Size::MAX_CONTENT)?;
    let capture_layout = tree.layout(capture)?;
    if (capture_layout.size.width - width).abs() > 0.01 {
        return Err(TaffyError::InvalidInputNode(root));
    }
    Ok([nav, list, editor]
        .iter()
        .map(|node| tree.layout(*node).unwrap().size.width)
        .collect())
}

fn flex_toolbar() -> Result<Vec<f32>, TaffyError> {
    let mut tree = TaffyTree::<()>::new();
    let field = tree.new_leaf(Style {
        flex_grow: 1.0,
        flex_shrink: 1.0,
        flex_basis: length(240.0),
        ..Default::default()
    })?;
    let button = tree.new_leaf(Style {
        size: Size {
            width: length(80.0),
            height: length(32.0),
        },
        ..Default::default()
    })?;
    let root = tree.new_with_children(
        Style {
            display: Display::Flex,
            size: Size {
                width: length(432.0),
                height: length(40.0),
            },
            gap: Size {
                width: length(8.0),
                height: zero(),
            },
            ..Default::default()
        },
        &[field, button],
    )?;
    tree.compute_layout(root, Size::MAX_CONTENT)?;
    Ok(vec![
        tree.layout(field)?.size.width,
        tree.layout(button)?.size.width,
    ])
}

fn wrapped_chips() -> Result<usize, TaffyError> {
    let mut tree = TaffyTree::<()>::new();
    let mut children = Vec::new();
    for _ in 0..5 {
        children.push(tree.new_leaf(Style {
            size: Size {
                width: length(130.0),
                height: length(24.0),
            },
            ..Default::default()
        })?);
    }
    let root = tree.new_with_children(
        Style {
            display: Display::Flex,
            flex_wrap: FlexWrap::Wrap,
            size: Size {
                width: length(300.0),
                height: auto(),
            },
            gap: Size {
                width: length(8.0),
                height: length(6.0),
            },
            ..Default::default()
        },
        &children,
    )?;
    tree.compute_layout(root, Size::MAX_CONTENT)?;
    let mut ys: Vec<i32> = children
        .iter()
        .map(|node| tree.layout(*node).unwrap().location.y.round() as i32)
        .collect();
    ys.sort_unstable();
    ys.dedup();
    Ok(ys.len())
}

struct TextContext {
    utf16_units: usize,
}

fn wrapped_text() -> Result<(f32, f32, usize), TaffyError> {
    let mut tree = TaffyTree::<TextContext>::new();
    let text = tree.new_leaf_with_context(Style::default(), TextContext { utf16_units: 113 })?;
    let root = tree.new_with_children(
        Style {
            display: Display::Flex,
            flex_direction: FlexDirection::Column,
            size: Size {
                width: length(210.0),
                height: auto(),
            },
            ..Default::default()
        },
        &[text],
    )?;
    let mut calls = 0;
    tree.compute_layout_with_measure(root, Size::MAX_CONTENT, |inputs, _, context, style| {
        calls += 1;
        let units = context.map(|value| value.utf16_units).unwrap_or_default();
        taffy::compute_leaf_layout(
            inputs,
            style,
            |_, _| 0.0,
            |known, available| {
                let max_width = match available.width {
                    AvailableSpace::Definite(value) => value,
                    _ => units as f32 * 7.0,
                };
                let width = known.width.unwrap_or((units as f32 * 7.0).min(max_width));
                let lines = ((units as f32 * 7.0) / width.max(1.0)).ceil();
                Size {
                    width,
                    height: known.height.unwrap_or(lines * 18.0),
                }
            },
        )
    })?;
    let layout = tree.layout(text)?;
    Ok((layout.size.width, layout.size.height, calls))
}

fn nested_scroll() -> Result<(f32, f32, f32), TaffyError> {
    let mut tree = TaffyTree::<()>::new();
    let header = tree.new_leaf(Style {
        grid_row: line(1),
        size: Size {
            width: auto(),
            height: length(48.0),
        },
        ..Default::default()
    })?;
    let content = tree.new_leaf(Style {
        flex_shrink: 0.0,
        size: Size {
            width: auto(),
            height: length(1200.0),
        },
        ..Default::default()
    })?;
    let viewport = tree.new_with_children(
        Style {
            display: Display::Flex,
            flex_direction: FlexDirection::Column,
            grid_row: line(2),
            min_size: Size {
                width: zero(),
                height: zero(),
            },
            overflow: Point {
                x: Overflow::Clip,
                y: Overflow::Scroll,
            },
            ..Default::default()
        },
        &[content],
    )?;
    let root = tree.new_with_children(
        Style {
            display: Display::Grid,
            size: Size {
                width: length(320.0),
                height: length(520.0),
            },
            grid_template_rows: vec![length(48.0), fr(1.0)],
            ..Default::default()
        },
        &[header, viewport],
    )?;
    tree.compute_layout(root, Size::MAX_CONTENT)?;
    let viewport_layout = tree.layout(viewport)?;
    let content_layout = tree.layout(content)?;
    Ok((
        viewport_layout.size.height,
        content_layout.size.height,
        viewport_layout.scrollable_overflow_rect.bottom,
    ))
}

fn cumulative_rounding(total: f32, tracks: usize, scale: f32) -> Vec<f32> {
    let mut result = Vec::new();
    let mut prior = 0.0;
    for index in 1..=tracks {
        let edge = ((total * index as f32 / tracks as f32) * scale).round() / scale;
        result.push(edge - prior);
        prior = edge;
    }
    result
}
