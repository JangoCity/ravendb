﻿/// <reference path="../../../typings/tsd.d.ts"/>
import virtualColumn = require("widgets/virtualGrid/columns/virtualColumn");

/**
 * A virtual row. Contains an element displayed as a row in the list view. Gets recycled as the list view scrolls in order to create and manage fewer elements.
 */
class virtualListRow<T> {
    private item: T | null = null; // The last item populated into this virtual list row.
    readonly element: JQuery;
    private _top = -9999;
    private _index = -1;
    private even: boolean | null = null;

    private _height: number;
    
    private _htmlProvider: (item: T) => string;
    
    constructor(htmlProvider: (item: T) => string) {
        this.element = $(`<div class="virtual-row" style="height: ${this._height}px; top: ${this.top}px"></div>`);
        this._htmlProvider = htmlProvider;
    }

    get top(): number {
        return this._top;
    }
    
    get height(): number {
        return this._height;
    }

    get data(): T {
        return this.item;
    }

    /**
     * Gets the index of the row this virtual row is displaying.
     */
    get index(): number {
        return this._index;
    }

    get hasData(): boolean {
        return !!this.item;
    }

    populate(item: T | null, rowIndex: number, top: number, height: number) {
        // Optimization: don't regenerate this row HTML if nothing's changed since last render.
        const alreadyDisplayingData = !!item && this.item === item && this._index === rowIndex;
        if (!alreadyDisplayingData) {
            this.item = item;
            this._index = rowIndex;

            // If we have data, fill up this row content.
            if (item) {
                this.element.html(this._htmlProvider(item));
            } else {
                this.element.text("");
            }

            // Update the "even" status. Used for striping the virtual rows.
            const newEvenState = rowIndex % 2 === 0;
            const hasChangedEven = this.even !== newEvenState;
            if (hasChangedEven) {
                this.even = newEvenState;
                this.element.toggleClass("even", this.even);
            }

            // Move it to its proper position.
            this.setElementTop(top);
            this.setElementHeight(height);
        }
    }

    reset() {
        this.item = null;
        this.setElementTop(-9999);
        this._index = -1;
        this.even = null;
        this.element.text("");
    }

    private setElementTop(val: number) {
        this._top = val;
        this.element.css("top", val + "px");
    }
    
    private setElementHeight(val: number) {
        this._height = val;
        this.element.css("height", _.isUndefined(val) ? "auto" : val + "px");
    }
}

export = virtualListRow;
