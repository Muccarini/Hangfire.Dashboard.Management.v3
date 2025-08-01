(function (hangfire) {

	hangfire.Management = (function () {
		function Management() {
			this._initialize();
		}
		Management.prototype._initialize = function () {
			$(".commands-panel.hide, .commands-options.hide").each(function () {
				$(this).hide().removeClass('hide');
			})

			InitializeTD(this);
		
			function InitializeTD($scope){
				let defaultTempusDominusOptions = { localization: tempusDominus.DefaultOptions.localization }
				let controlConfigs = $("#hdmConfig").data("controlconfigs");
				if (controlConfigs) {
					if (controlConfigs.dateTimeOpts) {
						if (controlConfigs.dateTimeOpts.locale !== 'default') {
							if (tempusDominus.locales[controlConfigs.dateTimeOpts.locale]) {
								tempusDominus.DefaultOptions.localization = $.extend(tempusDominus.DefaultOptions.localization, tempusDominus.locales[controlConfigs.dateTimeOpts.locale].localization);
							}
						}
					}
				}

				$("div[id$='_datetimepicker']").each(function () {
					let tdElement = $(this)

					let options = tdElement.data('td_options');

					let tdInput = $(this).find('input')

					if (tdInput) {
						let defaultVal = tdElement.data('td_value');
						let td;

						td = new tempusDominus.TempusDominus(this);

						if (options) {
							//console.log('Found Options: ', options)
							if (options.localization) {
								//console.log('Old Localization: ', options.localization);
								let newLocalization = $.extend({}, tempusDominus.DefaultOptions.localization, options.localization);
								options.localization = newLocalization;
								//console.log('New Localization: ', options.localization);
							}
							td.updateOptions(options, true);
						}

						tdElement
							.on('change.td', function (tdEvent) {
								//console.log(tdEvent.date.toISOString())
								if (tdEvent.date) {
									tdElement.data('date', tdEvent.date.toISOString());
									tdElement.attr('date', tdEvent.date.toISOString());
								}
								else {
									tdElement.data('date', null);
									tdElement.attr('date', null);
								}
							})
							.on('error.td', function (tdEvent) {
								console.error('Error from Tempus Dominus : ', tdEvent)
							});

						if (defaultVal) {
							let jsDate = new Date(defaultVal);
							if (!isNaN(jsDate)) {
								let tdDate = tempusDominus.DateTime.convert(jsDate);
								if (tempusDominus.DateTime.isValid(tdDate)) {
									td.dates.setValue(tdDate);
								} else {
									console.log("not valid format (Tempus Dominus DateTime invalid)");
								}
							} else {
								console.log("not valid format (JS Date invalid)");
							}
						}
					}
				});

				$('input.time[data-inputmask]').each(function () {
					Inputmask($(this).data('inputmask')).mask(this);
				});
			}

			$('.load-history-btn').on('click', function () {
				var $dropdown = $(this).closest('.job-history').find('.job-history-dropdown');
				var selectedId = $dropdown.val();

				if (!selectedId) {
					alert('Please select a configuration first.');
					return;
				}

				if(selectedId == 'Reset'){
					if (!confirm('Are you sure you want to discard the current parameters?')) { return; }
					// Redirect without querystring
					window.location.search = '';
					return;
				}
				else if (!confirm('Are you sure you want to reset the current parameters and load the previous parameters from the job with ID: ' + selectedId + ' ?')) { return; }

				// Redirect with querystring
				window.location.search = $.param({
					jobHistoryId: selectedId
				});

			});

  			$('.panel-body.list-element-container').on('click', '.element-adder', function (e) {
				e.stopPropagation();
  			  	var $elementContainer = $(this).closest('.panel-body.list-element-container');
  			  	var $elements = $elementContainer.children('[data-index]:not(.d-none)');
  			  	var $template = $elementContainer.children('[data-index="0"]');
		
  			  	if ($elements.length === 0) {
  			  	  $template.removeClass('d-none');
  			  	} else {
  			  	  addListElement($elementContainer, $elements.length);
  			  	}

			  	updateListLength($elementContainer);
  			});
		
  			$('.panel-body.list-element-container').on('click', '.element-deleter', function () {
  			  var $elementContainer = $(this).closest('.panel-body.list-element-container');
  			  var $elements = $elementContainer.children('[data-index]');
  			  var $elementToDelete = $(this).closest('[data-index]');
  			  if ($elements.length === 1) {
  			    $elementToDelete.addClass('d-none');
  			  } else {
				var deletedIndex = parseInt($elementToDelete.attr('data-index'), 10);
    			$elementToDelete.remove();
    			$elements.each(function () {
					var $el = $(this);
    			  var idx = parseInt($el.attr('data-index'), 10);
    			  if (idx > deletedIndex) {
					isLast = (idx === $elements.length - 1) ? true : false;
    			    updateListElementIndex($el, idx - 1, isLast);
    			  }
    			});
  			  }
			  updateListLength($elementContainer);
  			});

			function addListElement($container, index) {
			  var $last = $container.children('[data-index]:not(.d-none)').last();
			  var $clone = $last.clone(false, true);
			  updateListElementIndex($clone, index, true);
			  $last.after($clone);
			  InitializeTD($clone);
			}

			function replaceListIndexAtDepth(str, newIndex, depth) {
			  const matches = [...str.matchAll(/(_list_)(\d+)/g)];
			  if (matches.length < depth) return str;
			  const match = matches[depth];
			  if (!match) return str;
			  return (
			    str.slice(0, match.index) +
			    match[1] + newIndex +
			    str.slice(match.index + match[0].length)
			  );
			}

			function updateListLength($elementsContainer) {
    			var count = $elementsContainer.children('[data-index]').not('.d-none').length;
    			$elementsContainer.attr('data-list-length', count);
			}

			function updateListElementIndex($element, index) {
			  	var depth = parseInt($element.attr('data-depth'), 10);
			  	$element.attr('data-index', index);
			
			  	// ID 
			  	$element.find('[id]').each(function () {
			  	  var $el = $(this);
			  	  var id = $el.attr('id');
			  	  if (!id) return;
			  	  var newId = replaceListIndexAtDepth(id, index, depth);
			  	  $el.attr('id', newId);
			  	});
		  
			  	// NAME
			  	$element.find('[name]').each(function () {
			  	  var $el = $(this);
			  	  var name = $el.attr('name');
			  	  if (!name) return;
			  	  var newName = replaceListIndexAtDepth(name, index, depth);
			  	  $el.attr('name', newName);
			  	});
		  
			  	// FOR
			  	$element.find('label[for]').each(function () {
			  	  var $el = $(this);
			  	  var labelFor = $el.attr('for');
			  	  if (!labelFor) return;
			  	  var newFor = replaceListIndexAtDepth(labelFor, index, depth);
			  	  $el.attr('for', newFor);
			  	});

			  	// data-index
			  	$element.children('[data-index]').each(function () {
			  	  $(this).attr('data-index', index);
			  	});

				//class
				$element.find('[class]').each(function () {
				    var $el = $(this);
				    var classList = $el.attr('class');
				    if (classList && /_list_\d+/.test(classList)) {
				        $el.attr('class', replaceListIndexAtDepth(classList, index, depth));
				    }
				});

				$element.find('ul[data-optionsid]').each(function () {
				  var $el = $(this);
				  var optsId = $el.attr('data-optionsid');
				  if (optsId) $el.attr('data-optionsid', replaceListIndexAtDepth(optsId, index, depth));
				});

				$element.find('ul[aria-labelledby]').each(function () {
				  var $el = $(this);
				  var labelledBy = $el.attr('aria-labelledby');
				  if (labelledBy) $el.attr('aria-labelledby', replaceListIndexAtDepth(labelledBy, index, depth));
				});

				$element.find('span.input-data-list-text').each(function () {
				  var $el = $(this);
				  var classList = $el.attr('class');
				  if (classList) $el.attr('class', replaceListIndexAtDepth(classList, index, depth));
				});

				$element.find('a[data-target-panel-id]').each(function () {
				  var $el = $(this);
				  var targetPanelId = $el.attr('data-target-panel-id');
				  if (targetPanelId) $el.attr('data-target-panel-id', replaceListIndexAtDepth(targetPanelId, index, depth));
				});
		  
			  	$element.find('.panel-heading[role="button"]').each(function () {
			  	  var href = $(this).attr('href');
			  	  if (href) {
			  	    var newHref = replaceListIndexAtDepth(href, index, depth);
			  	    $(this).attr('href', newHref);
			  	  }
			  	  var ariaControls = $(this).attr('aria-controls');
			  	  if (ariaControls) {
			  	    var newAria = replaceListIndexAtDepth(ariaControls, index, depth);
			  	    $(this).attr('aria-controls', newAria);
			  	  }
			  	});
			}

			$('.hdm-management').each(function () {
				var container = this;

				var showCommandsPanelOptions = function (commandsType) {

					$(".commands-panel", container).hide();
					$(".commands-panel." + commandsType, container).show();

					$(".commands-options", container).hide();
					$(".commands-options." + commandsType, container).show();
					//data-commands-type="Enqueue" data-id="@(id)"
					$(".commandsType." + id).html($("a[data-commands-type='" + commandsType + "']", container).html());
				};

				var $this = $(this);
				var id = $this.data("id");

                $(this).on('click', '.impl-selector-options .option', function (e) {
                    e.preventDefault();
                    var $option = $(this);
                    var optionText = $option.data('optiontext');
					var optionValue = $this.data('optionvalue');
                    var targetPanelId = $option.data('target-panel-id');
                    
                    var optionsId = $option.closest('ul.impl-selector-options').data('optionsid');
                    var $button = $('#' + optionsId + '.hdm-impl-selector-button', container);

                    $button.find('.input-data-list-text').text(optionText);
					$button.data('selectedvalue', optionValue); 
					$button.attr('data-selectedvalue', optionValue);

                    $('.impl-panels-for-' + optionsId, container).addClass('d-none');

                    if (targetPanelId) {
						var $targetPanel = $('#' + targetPanelId, container);
						$targetPanel.removeClass('d-none');
                    }
                });

				$(this).on('click', '.data-list-options .option',
					function (e) {
						e.preventDefault();
						var $this = $(this);
						var optionValue = $this.data('optionvalue');
						var optionText = $this.data('optiontext');

						var optionsId = $this.parents('ul').data('optionsid');
						var $button = $('#' + optionsId, container);

						$button.data('selectedvalue', optionValue);
						$button.attr('data-selectedvalue', optionValue);
						$button.find('.input-data-list-text').text(optionText);

					});

				$(this).on('click', '.commands-type',
					function (e) {
						var $this = $(this);
						var commandsType = $this.data('commands-type');
						showCommandsPanelOptions(commandsType);
						e.preventDefault();
					});

				$(this).on('click', '.hdm-management-input-commands',
					function (e) {
						var $this = $(this);
						var confirmText = $this.data('confirm');

						var id = $this.attr("input-id");
						var type = $this.attr("input-type");
						var send = { id: id, type: type };

						$('.list-element-container', container).each(function() {
						    var $list = $(this);
						    var listId = $list.attr('id');
						    var listLength = $list.attr('data-list-length');
						    if (listId && listLength !== undefined) {
						        send[listId] = listLength;
						    }
						});

						$("input.hdm-job-input.hdm-input-checkbox[id^='" + id + "']", container).each(function () {
							//console.log('Reading Checkbox Input: ' + $(this).prop('id') + ' => ' + $(this).is(':checked'));

							if ($(this).is(':checked')) {
								send[$(this).prop('id')] = "on";
							}
						});

						$("input.hdm-job-input.hdm-input-text[id^='" + id + "']", container).each(function () {
							//console.log('Reading Text Input: ' + $(this).prop('id') + ' => ' + $(this).val());
							send[$(this).prop('id')] = $(this).val();
						});

						$("input.hdm-job-input.hdm-input-number[id^='" + id + "']", container).each(function () {
							//console.log('Reading Number Input: ' + $(this).prop('id') + ' => ' + $(this).val());
							send[$(this).prop('id')] = $(this).val();
						});

						$("textarea.hdm-job-input[id^='" + id + "']", container).each(function () {
							//console.log('Reading TextArea Input: ' + $(this).prop('id') + ' => ' + $(this).val());
							send[$(this).prop('id')] = $(this).val();
						});

						$("select.hdm-job-input[id^='" + id + "']", container).each(function () {
							//console.log('Reading Select Input: ' + $(this).prop('id') + ' => ' + $(this).val());
							send[$(this).prop('id')] = $(this).val();
						});

						$(".hdm-job-input.hdm-input-datalist[id^='" + id + "']", container).each(function () {
							//console.log('Reading DataList Input: ' + $(this).prop('id') + ' => ' + $(this).data('selectedvalue'));
							//send[$(this).prop('id')] = $(this).data('selectedvalue'); //this does not work after cloning.
							send[$(this).prop('id')] = $(this).attr('data-selectedvalue'); //change name
						});

						$("div.hdm-job-input-container.hdm-input-date-container[id^='" + id + "']", container).each(function () {
							//console.log('Reading Date Input: ' + $(this).prop('id') + ' => ' + $(this).data('date'));
							//send[$(this).prop('id')] = $(this).data('date');
							send[$(this).prop('id')] = $(this).attr('date');
						});


						if (send.type === 'Enqueue') {
							// Nothing extra to read here
						}
						else if (send.type === 'ScheduleDateTime') {
							let sdtTd = $(".commands-options.ScheduleDateTime .hdm-execution-input[id^='" + id + "']", container)
							if (sdtTd.length > 0) {
								let sdtInput = $($(sdtTd).find('input')[0])
								//console.log('Reading Schedule Date Input: ' + sdtInput.prop('id') + ' => ' + sdtInput.val());
								send[sdtInput.prop('id')] = sdtTd.data('date');
							}
							else {
								Hangfire.Management.alertError(id, 'Unable to find controls for ScheduleDateTime');
								return;
							}
						}
						else if (send.type === 'ScheduleTimeSpan') {
							let sts = $(".commands-options.ScheduleTimeSpan .hdm-execution-input[id^='" + id + "']", container)
							if (sts.length > 0) {
								//console.log('Reading Schedule Timespan Input: ' + sts.prop('id') + ' => ' + sts.val());
								send[sts.prop('id')] = sts.val();
							}
							else {
								Hangfire.Management.alertError(id, 'Unable to find controls for ScheduleTimeSpan');
								return;
							}

							// This adds a special parameter for when and execute button's drop down is used to specify the schedule.
							if ($this.data('schedule')) {
								//console.log('Overriding Input with Predefined Option: ' + sts.prop('id') + ' => ' + $this.data("schedule"));
								send[sts.prop('id')] = $this.data("schedule");
							}
						}
						else if (send.type === 'CronExpression') {
							let cronExpressionInput = $(".commands-options.CronExpression .hdm-execution-input-exp[id^='" + id + "']", container)
							if (cronExpressionInput.length === 1) {
								let val = cronExpressionInput.val();
								//console.log('Reading Cron Expression Input: ' + cronExpressionInput.prop('id') + ' => ' + val);
								send[cronExpressionInput.prop('id')] = val;
							}
							else {
								Hangfire.Management.alertError(id, 'Unable to find control for Cron Expression Input');
								return;
							}

							// This adds a special parameter for when and execute button's drop down is used to specify the schedule.
							if ($this.data('schedule')) {
								//console.log('Overriding Input with Predefined Option: ' + cronExpressionInput.prop('id') + ' => ' + $this.data("schedule"));
								send[cronExpressionInput.prop('id')] = $this.data("schedule");
							}

							let cronNameInput = $(".commands-options.CronExpression .hdm-execution-input-name[id^='" + id + "']", container)
							if (cronNameInput.length === 1) {
								//console.log('Reading Cron Name Input: ' + cronNameInput.prop('id') + ' => ' + cronNameInput.val());
								send[cronNameInput.prop('id')] = cronNameInput.val();
							}
						}
						else {
							throw 'Unknown Execution Type'
						}

						//console.log('form data: ', send);
						$('#' + id + '_success, #' + id + '_error').empty();
						if (!confirmText || confirm(confirmText)) {
							$this.prop('disabled');
							$this.button('loading');

							$.post($this.data('url'), send, function (data) {
								let taskType = "An Immediate";
								if (send.type === "ScheduleDateTime") { taskType = "A Scheduled"; }
								else if (send.type === "ScheduleTimeSpan") { taskType = "A Delayed"; }
								else if (send.type === "CronExpression") { taskType = "A Recurring"; }
								Hangfire.Management.alertSuccess(id, taskType + " Execution Task has been created. <a href=\"" + data.jobLink + "\">View Job</a>");
							}).fail(function (xhr) {
								var error = 'Unknown Error';

								try {
									error = JSON.parse(xhr.responseText).errorMessage;
								} catch (e) { /* ignore error */ }

								Hangfire.Management.alertError(id, error);
							}).always(function () {
								$this.removeProp('disabled');
								$this.button('reset');
							});
						}

						e.preventDefault();
					});

				$('.input-group *[title], .btn-group *[title]').tooltip('destroy');
				$('.input-group *[title], .btn-group *[title]').tooltip({ container: 'body' });
			});
		};

		Management.alertSuccess = function (id, message) {
			$('#' + id + '_success')
				.html('<div class="alert alert-success"><a class="close" data-dismiss="alert">×</a><strong>Task Created! </strong><span>' +
					message +
					'</span></div>');
		}

		Management.alertError = function (id, message) {
			$('#' + id + '_error')
				.html('<div class="alert alert-danger"><a class="close" data-dismiss="alert">×</a><strong>Error! </strong><span>' +
					message +
					'</span></div>');
		}

		return Management;

	})();

})(window.Hangfire = window.Hangfire || {});


//console.log('Hangfire Dashboard Management Bundle Starting');
new Hangfire.Management();
