﻿using System;
using System.Collections.Generic;
using System.Globalization;
using Lucene.Net.Documents;
using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Server.Documents.Indexes.Persistance.Lucene.Documents.Fields;
using Raven.Server.Json;

namespace Raven.Server.Documents.Indexes.Persistance.Lucene.Documents
{
    public class LuceneDocumentConverter : IDisposable
    {
        private static readonly FieldCacheKeyEqualityComparer Comparer = new FieldCacheKeyEqualityComparer();

        private readonly Dictionary<FieldCacheKey, CachedFieldItem<Field>> _fieldsCache = new Dictionary<FieldCacheKey, CachedFieldItem<Field>>(Comparer);

        private readonly Dictionary<FieldCacheKey, CachedFieldItem<NumericField>> _numericFieldsCache = new Dictionary<FieldCacheKey, CachedFieldItem<NumericField>>(Comparer);

        private readonly global::Lucene.Net.Documents.Document _document = new global::Lucene.Net.Documents.Document();

        private readonly IndexField[] _fields;
        private bool _fieldsAdded;

        public LuceneDocumentConverter(IndexField[] fields)
        {
            _fields = fields;
        }

        // returned document needs to be written do index right after conversion because the same cached instance is used here
        public global::Lucene.Net.Documents.Document ConvertToCachedDocument(Document document)
        {
            foreach (var field in GetFields(document))
            {
                if (_fieldsAdded == false)
                    _document.Add(field);
                else
                {
                    // one lucene document converter is binded to one index instance which means that 
                    // fields will be the same and values can be just be overwritten
                    // for now let us just iterate over fields to update their values // TODO arek
                }
            }

            _fieldsAdded = true;

            return _document;
        }

        /// <summary>
        /// This method generate the fields for indexing documents in lucene from the values.
        /// Given a name and a value, it has the following behavior:
        /// * If the value is enumerable, index all the items in the enumerable under the same field name
        /// * If the value is null, create a single field with the supplied name with the unanalyzed value 'NULL_VALUE'
        /// * If the value is string or was set to not analyzed, create a single field with the supplied name
        /// * If the value is date, create a single field with millisecond precision with the supplied name
        /// * If the value is numeric (int, long, double, decimal, or float) will create two fields:
        ///		1. with the supplied name, containing the numeric value as an unanalyzed string - useful for direct queries
        ///		2. with the name: name +'_Range', containing the numeric value in a form that allows range queries
        /// </summary>
        private IEnumerable<AbstractField> GetFields(Document document)
        {
            yield return GetOrCreateField(Constants.DocumentIdFieldName, null, document.Key, Field.Store.YES, Field.Index.NOT_ANALYZED_NO_NORMS);
            
            foreach (var field in _fields)
            {
                var path = field.Name;

                var indexing = field.Indexing.GetLuceneValue(@default: Field.Index.ANALYZED_NO_NORMS);
                var storage = field.Storage.GetLuceneValue(@default: Field.Store.NO);

                object value;
                if (document.Data.TryGetMember(path, out value) == false)
                {
                    yield return GetOrCreateField(path, Constants.NullValue, null, storage, Field.Index.NOT_ANALYZED_NO_NORMS);
                    continue;
                }

                if (Equals(value, string.Empty))
                {
                    yield return GetOrCreateField(path, Constants.EmptyString, null, storage, Field.Index.NOT_ANALYZED_NO_NORMS);
                    continue;
                }

                var lazyStringValue = value as LazyStringValue;

                if (lazyStringValue != null)
                {
                    yield return GetOrCreateField(path, null, lazyStringValue, storage, indexing);
                    continue;
                }

                if (value is LazyDoubleValue)
                {
                    yield return GetOrCreateField(path, null, ((LazyDoubleValue) value).Inner, storage, indexing);
                }
                else if (value is IConvertible) // we need this to store numbers in invariant format, so JSON could read them
                {
                    yield return GetOrCreateField(path, ((IConvertible) value).ToString(CultureInfo.InvariantCulture), null, storage, indexing);
                }

                foreach (var numericField in GetOrCreateNumericField(field, value, storage))
                    yield return numericField;
            }
        }

        private Field GetOrCreateField(string name, string value, LazyStringValue lazyValue, Field.Store store, Field.Index index, Field.TermVector termVector = Field.TermVector.NO)
        {
            var cacheKey = new FieldCacheKey(name, index, store, termVector, new int[0]); // TODO [ppekrol]

            Field field;
            CachedFieldItem<Field> cached;

            if (_fieldsCache.TryGetValue(cacheKey, out cached) == false)
            {
                LazyStringReader reader = null;

                if (lazyValue != null && store.IsStored() == false && index.IsIndexed())
                    field = new Field(name, (reader = new LazyStringReader()).GetTextReaderFor(lazyValue));
                else
                    field = new Field(name, value ?? (reader = new LazyStringReader()).GetStringFor(lazyValue), store, index);

                field.Boost = 1;
                field.OmitNorms = true;

                _fieldsCache[cacheKey] = new CachedFieldItem<Field>
                {
                    Field = field,
                    LazyStringReader = reader
                };
            }
            else
            {
                field = cached.Field;

                if (store.IsStored() == false && index.IsIndexed())
                    field.SetValue(cached.LazyStringReader.GetTextReaderFor(lazyValue));
                else
                    field.SetValue(value ?? cached.LazyStringReader.GetStringFor(lazyValue));
            }

            return field;
        }

        private IEnumerable<AbstractField> GetOrCreateNumericField(IndexField field, object value, Field.Store storage, Field.TermVector termVector = Field.TermVector.NO)
        {
            var fieldName = field.Name + "_Range";

            var cacheKey = new FieldCacheKey(field.Name, null, storage, termVector, new int[0]);// TODO arek multipleItemsSameFieldCount.ToArray());

            NumericField numericField;
            CachedFieldItem<NumericField> cached;

            if (_numericFieldsCache.TryGetValue(cacheKey, out cached) == false)
            {
                _numericFieldsCache[cacheKey] = cached = new CachedFieldItem<NumericField>
                {
                    Field = numericField = new NumericField(fieldName, storage, true)
                };
            }
            else
            {
                numericField = cached.Field;
            }

            var sortOption = field.SortOption;

            if (value is LazyDoubleValue)
            {
                var doubleValue = double.Parse(cached.LazyStringReader.GetStringFor(((LazyDoubleValue) value).Inner));

                if (sortOption == SortOptions.Float)
                    yield return numericField.SetFloatValue(Convert.ToSingle(doubleValue));
                else if (sortOption == SortOptions.Int)
                    yield return numericField.SetIntValue(Convert.ToInt32(doubleValue));
                else if (sortOption == SortOptions.Long)
                    yield return numericField.SetLongValue(Convert.ToInt64(doubleValue));
                else
                    yield return numericField.SetDoubleValue(doubleValue);
            }
            else if (value is long)
            {
                if (sortOption == SortOptions.Double)
                    yield return numericField.SetDoubleValue((long)value);
                else if (sortOption == SortOptions.Float)
                    yield return numericField.SetFloatValue((long)value);
                else if (sortOption == SortOptions.Int)
                    yield return numericField.SetIntValue(Convert.ToInt32((long)value));
                else
                    yield return numericField.SetLongValue((long)value);
            }
        }

        public void Dispose()
        {
            foreach (var cachedFieldItem in _fieldsCache.Values)
            {
                cachedFieldItem.Dispose();
            }
        }
    }
}