////var Select2Helper = (function () {

////    function init() {
////        $('.select2').each(function () {
////            const $el = $(this);
////            const url = $el.data("url");
////            const parent = $el.data("parent");

////            $el.select2({
////                width: "100%",
////                allowClear: true,
////                placeholder: $el.data("placeholder"),

////                ajax: {
////                    url: url,
////                    dataType: 'json',
////                    delay: 300,

////                    data: function (params) {
////                        let query = { term: params.term };
////                        if (parent) {
////                            query.deptCd = $(parent).val();
////                        }
////                        return query;
////                    },

////                    processResults: function (data) {
////                        return { results: data };
////                    }
////                }
////            });
////        });
////    }

////    function setValue(selector, id, text) {
////        if (!id) return;
////        const option = new Option(text, id, true, true);
////        $(selector).append(option).trigger('change');
////    }

////    function resetChild(parent, child) {
////        $(parent).on('change', function () {
////            $(child).val(null).trigger('change');
////        });
////    }

////    return {
////        init,
////        setValue,
////        resetChild
////    };

////})();
var Select2Helper = (function () {
    function init($scope) {
        $scope = $scope || $(document);

        $scope.find('.select2').each(function () {
            const $el = $(this);
            const url = $el.data('url');
            const parent = $el.data('parent');
            const selectedId = $el.data('selected-id');
            let selectedText = $el.data('selected-text');

            $el.select2({
                width: '100%',
                placeholder: $el.data('placeholder'),
                allowClear: true,
                ajax: url ? {
                    url: url,
                    dataType: 'json',
                    delay: 300,
                    data: function (params) {
                        let query = { term: params.term };
                        if (parent) query.deptCd = $(parent).val();
                        // nếu params.term rỗng và có selectedId, gửi id để load text
                        if (!params.term && selectedId) query.id = selectedId;
                        return query;
                    },
                    processResults: function (data) { return { results: data }; }
                } : null
            });

            // Nếu có selectedId nhưng selectedText trống → gọi ajax lấy text
            if (selectedId && !selectedText && url) {
                $.ajax({
                    url: url,
                    dataType: 'json',
                    data: { id: selectedId }, // backend cần hỗ trợ param id
                    success: function (data) {
                        if (data && data.length > 0) {
                            const option = new Option(data[0].text, selectedId, true, true);
                            $el.append(option).trigger('change');
                        }
                    }
                });
            } else if (selectedId && selectedText) {
                // normal
                if ($el.find("option[value='" + selectedId + "']").length === 0) {
                    const option = new Option(selectedText, selectedId, true, true);
                    $el.append(option).trigger('change');
                } else {
                    $el.val(selectedId).trigger('change');
                }
            }
        });
    }

    return { init };
})();