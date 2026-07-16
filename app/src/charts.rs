//! Tiny dependency-free charts built from `border` rectangles (the same idiom
//! screen_posture uses for its graded bars) — a horizontal bar chart and a stacked
//! bar. Used by the Dashboard / Security Posture tile screens to visualize the numeric
//! metric tiles the sidecar already returns.

use crate::theme;
use windows_reactor::*;

const TRACK: f64 = 240.0;

/// A horizontal bar chart from (label, value) pairs. Bars scale to the largest value;
/// each row is `label · track(fill) · value`.
pub fn bar_chart(data: &[(String, f64)]) -> Element {
    let max = data.iter().map(|(_, v)| *v).fold(0.0_f64, f64::max).max(1.0);
    let rows: Vec<Element> = data
        .iter()
        .map(|(label, v)| {
            let frac = (v / max).clamp(0.0, 1.0);
            let fill = border(Element::Empty)
                .background(theme::BRAND_BRIGHT)
                .corner_radius(3.0)
                .height(14.0)
                .width((TRACK * frac).max(2.0))
                .horizontal_alignment(HorizontalAlignment::Left);
            let track = border(Element::from(fill))
                .background(theme::SURFACE_2)
                .corner_radius(3.0)
                .height(14.0)
                .width(TRACK);
            grid((
                Element::from(
                    caption(label.clone()).font_family(theme::FONT_UI).foreground(theme::TEXT_2),
                )
                .grid_column(0),
                Element::from(track).grid_column(1),
                Element::from(
                    caption(format!("{}", *v as i64))
                        .font_family(theme::FONT_MONO)
                        .foreground(theme::TEXT_3),
                )
                .grid_column(2),
            ))
            .columns([GridLength::Star(1.0), GridLength::Pixel(TRACK), GridLength::Pixel(56.0)])
            .column_spacing(10.0)
            .into()
        })
        .collect();
    vstack(rows).spacing(8.0).into()
}

/// A single stacked horizontal bar (segments proportional to their value) with a
/// coloured legend beneath. Zero-value segments are dropped from the bar but kept in
/// the legend so the reader still sees the "0".
pub fn stacked_bar(segments: &[(String, f64, Color)]) -> Element {
    const W: f64 = 320.0;
    let total = segments.iter().map(|(_, v, _)| *v).sum::<f64>().max(1.0);
    let segs: Vec<Element> = segments
        .iter()
        .filter(|(_, v, _)| *v > 0.0)
        .map(|(_, v, c)| {
            border(Element::Empty)
                .background(*c)
                .height(18.0)
                .width((v / total * W).max(1.0))
                .into()
        })
        .collect();
    let bar = border(Element::from(hstack(segs)))
        .corner_radius(4.0)
        .background(theme::SURFACE_2)
        .horizontal_alignment(HorizontalAlignment::Left);
    let legend: Vec<Element> = segments
        .iter()
        .map(|(label, v, c)| {
            hstack((
                Element::from(
                    border(Element::Empty).background(*c).width(10.0).height(10.0).corner_radius(2.0),
                ),
                Element::from(
                    caption(format!("{} {}", label, *v as i64))
                        .foreground(theme::TEXT_3)
                        .font_family(theme::FONT_UI),
                ),
            ))
            .spacing(6.0)
            .into()
        })
        .collect();
    vstack((Element::from(bar), Element::from(hstack(legend).spacing(16.0))))
        .spacing(8.0)
        .into()
}
