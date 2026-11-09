using Apache.Arrow;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal interface IJsonColumnSource : IDisposable
{
    IArrowArray ReadColumn(int index, Field field, int length, JsonFieldBuffer scratch);
}
